namespace Aardworx.StereoStream

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open Aardvark.Base
open Aardvark.Rendering
open FSharp.Data.Adaptive
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.SceneGraph.IO
open Aardvark.Rendering.Text
open Aardvark.Glfw
open FSharp.NativeInterop
open System.Net.WebSockets


type PresentationMode =
    | Layered = 0
    | Left = 1
    | Right = 2
    | SideBySide = 3
    | Testing = 4

module Shader =

    open FShade
    open Aardvark.Rendering.Effects

    [<GLSLIntrinsic("gl_Layer")>]
    let layer() : int = onlyInShaderCode "gl_Layer"

    let leftRightTest (v : Vertex) =
        fragment {
            return { v with c = if layer() = 0 then V4d.IOOI elif layer() = 1 then V4d.OIOI else V4d.OOII }
        }

    let private leftSampler =
        sampler2d {
            texture uniform?LeftTexture
            filter Filter.MinMagLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let private rightSampler =
        sampler2d {
            texture uniform?RightTexture
            filter Filter.MinMagLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let applyLeftRight (v : Vertex) =
        fragment {
            let tc = V2d(v.tc.X, 1.0 - v.tc.Y)
            if layer() = 0 then
                return leftSampler.Sample(tc)
            else
               // return V4d.IOOI 
                return rightSampler.Sample(tc)
        }



    let private combinedSampler =
        sampler2d {
            texture uniform?CombinedTexture
            filter Filter.MinMagLinear
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }


    let extractLeftRight (v : Vertex) =
        fragment {
            let tc = V2d(v.tc.X * 0.5, 1.0 - v.tc.Y)
            if layer() = 0 then
                return combinedSampler.Sample(tc)
            else
               // return V4d.IOOI 
                return combinedSampler.Sample(tc + V2d(0.5,0.0))
        }


        
    let testLeftRight (v : Vertex) =
        fragment {
            let tc = V2d(v.tc.X, v.tc.Y)
            let index : int = uniform?Index
            if index = 0 then
                return leftSampler.Sample(tc).ZYXW
            else    
                //return V4d.IOOI 
                return rightSampler.Sample(tc).ZYXW
        }

    let manyModes (v : Vertex) =
        fragment {
            let tc = V2d(v.tc.X, v.tc.Y)
            let index : PresentationMode = uniform?PresentationMode
            match index with
            | PresentationMode.Layered -> 
                let index = layer() 
                if index = 0 then
                    return leftSampler.Sample(tc).ZYXW
                else    
                    return rightSampler.Sample(tc).ZYXW
            | PresentationMode.Left -> 
                return leftSampler.Sample(tc).ZYXW
            | PresentationMode.Right -> 
                return rightSampler.Sample(tc).ZYXW
            | PresentationMode.SideBySide -> 
                if tc.X < 0.5 then 
                    return leftSampler.Sample(tc * V2d(2.0, 1.0)).ZYXW
                else 
                    return rightSampler.Sample((tc - V2d(0.5,0.0)) * V2d(2.0, 1.0)).ZYXW
            | PresentationMode.Testing -> 
                if layer() = 0 then return V4d.IOOI else return V4d.OOII
            | _ -> return V4d.IIII
        }



module Main = 

    [<EntryPoint>]
    let main argv =

        Aardvark.Init()

        let dbg = Aardvark.Rendering.GL.DebugConfig.Normal
        use app = 
            try
                let windowInterop = StereoExperimental.stereo dbg
                new OpenGlApplication(dbg, windowInterop, None, false)
            with e -> 
                Log.warn "could not create stereo window. Trying mono."
                new OpenGlApplication()

        let win = app.CreateGameWindow({ WindowConfig.Default with samples = 8; width = 400; height= 400 })
        win.Title <- "Aardworx Stereo Streaming"
        //win.Cursor <- Cursor.None
        win.RenderAsFastAsPossible <- false


        let ls = win.Runtime.CreateStreamingTexture(false) 
        let rs = win.Runtime.CreateStreamingTexture(false)

        use cts = new CancellationTokenSource()

        let rec waitForConnection (ct : CancellationToken) =
            task {
                let ws = new System.Net.WebSockets.ClientWebSocket()
                let serverUri = new Uri("ws://localhost:4325");
                try
                    do! ws.ConnectAsync(serverUri, ct) 
                    return ws
                with e -> 
                    Log.line "coult not connect: %A, retrying..." e.Message
                    do! Task.Delay(100)
                    return! waitForConnection ct
            }

        let readCompleteMessage (ws : ClientWebSocket) (ct : CancellationToken) (ms : MemoryStream) =
            task {
                ms.Seek(0L, SeekOrigin.Begin) |> ignore
                let buff = Array.zeroCreate 8192
                let mutable endOfMessage = false
                let mutable bytesRead = Some 0L
                while not endOfMessage do
                    let! r = ws.ReceiveAsync(ArraySegment(buff), ct)
                    ms.Write(buff, 0, r.Count)
                    if r.CloseStatus.HasValue then 
                        bytesRead <- None
                        endOfMessage <- true
                    else
                        endOfMessage <- r.EndOfMessage
                        bytesRead <- bytesRead |> Option.map (fun v -> v + int64 r.Count)


                ms.Seek(0L, SeekOrigin.Begin) |> ignore
                return bytesRead

            }
        
        let t = 
            task {
                use! ws = waitForConnection cts.Token
                let msg = System.Text.Encoding.UTF8.GetBytes("READY")
                do! ws.SendAsync(ArraySegment(msg), WebSocketMessageType.Text, true, cts.Token)


                while ws.State = WebSocketState.Open do
                    use mems0 = new MemoryStream()
                    use mems1 = new MemoryStream()
                    let! img0 = readCompleteMessage ws cts.Token mems0
                    let! img1 = readCompleteMessage ws cts.Token mems1
                    match img0, img1 with
                    | Some r0, Some r1 when r0 > 0 && r1 > 0 ->
                        let img0 = PixImage.Load(mems0, PixImageDevil.Loader)
                        NativeTensor4.using ((img0 |> PixImage<byte>).Volume.AsTensor4()) (fun b -> 
                            ls.Update(img0.PixFormat, img0.Size, b.Pointer |> NativePtr.toNativeInt)
                        )
                        let img1 = PixImage.Load(mems1, PixImageDevil.Loader)
                        NativeTensor4.using ((img1 |> PixImage<byte>).Volume.AsTensor4()) (fun b -> 
                            rs.Update(img1.PixFormat, img1.Size, b.Pointer |> NativePtr.toNativeInt)
                        )
                        let msg = System.Text.Encoding.UTF8.GetBytes("UPDATEDTEXTURES")
                        do! ws.SendAsync(ArraySegment(msg), WebSocketMessageType.Text, true, cts.Token)
                    | _ -> 
                        let msg = System.Text.Encoding.UTF8.GetBytes("COULDNOTUPDATE")
                        do! ws.SendAsync(ArraySegment(msg), WebSocketMessageType.Text, true, cts.Token)
                        Log.warn "could not read images"
            }
        

        let index = cval 0
        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> 
                index.Value <- (index.Value + 1) % 2
                Log.line "%A" index.Value
            )
        ) |> ignore

        let presentationMode = cval PresentationMode.Layered
        win.Keyboard.KeyDown(Keys.M).Values.Add(fun _ -> 
            transact (fun _ -> 
                presentationMode.Value <- ((int presentationMode.Value + 1) % (int PresentationMode.Testing + 1)) |> unbox<PresentationMode>
                Log.line "mode=%A" presentationMode.Value
            )
        ) |> ignore

        let fullScreen = 
            Sg.fullScreenQuad 
            |> Sg.texture "LeftTexture" ls
            |> Sg.texture "RightTexture" rs
            |> Sg.uniform "Index" index
            |> Sg.uniform "PresentationMode" presentationMode
            |> Sg.shader {
                do! Shader.manyModes
            }
        //let fullScreen = 
        //    Sg.fullScreenQuad 
        //    |> Sg.texture "CombinedTexture" ls
        //    |> Sg.uniform "Index" index
        //    |> Sg.shader {
        //        //do! Shader.extractLeftRight
        //        do! Shader.testLeftRight
        //    }

        // assign the render task to our window...
        win.RenderTask <- win.Runtime.CompileRender(win.FramebufferSignature, fullScreen)
        win.Run()
        0