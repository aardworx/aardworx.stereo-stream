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
            if layer() = 0 then
                return leftSampler.Sample(v.tc)
            else
               // return V4d.IOOI 
                return rightSampler.Sample(v.tc)
        }


    let test (v : Vertex) =
        fragment {
            let u:int=uniform?Index
            if u = 0 then
                return leftSampler.Sample(v.tc)
            else    
                //return V4d.IOOI 
                return rightSampler.Sample(v.tc)
        }



module Main = 

    [<EntryPoint>]
    let main argv =

        Aardvark.Init()

        let dbg = Aardvark.Rendering.GL.DebugConfig.Normal
        let windowInterop = StereoExperimental.stereo dbg
        use app = new OpenGlApplication(dbg, windowInterop, None, false)
        //use app = new OpenGlApplication()
        let win = app.CreateGameWindow({ WindowConfig.Default with samples = 8; width = 400; height= 400 })
        win.Title <- "Quadbuffer Stereo. Use mouse to Rotate."
        //win.Cursor <- Cursor.None
        win.RenderAsFastAsPossible <- true

        let left : cval<ITexture> = cval NullTexture.Instance
        let right : cval<ITexture> = cval NullTexture.Instance

        let bytesToImage (size : V2i) (data : MemoryStream) =
            data.Position <- 0L
            let l = PixImage<byte>(Col.Format.RGBA, size)
            let bytes = size.X * size.Y * 4
            let read = data.Read(l.Array |> unbox<byte[]>, 0, bytes)
            if read <> bytes then Log.warn "strange"
            l
            
        let gotImage (img : WebServer.StereoImage) = 
            task {
                transact (fun _ ->
                    let l =  bytesToImage img.size img.left 
                    left.Value <- PixTexture2d(l)
                    let r = bytesToImage img.size img.right
                    right.Value <- PixTexture2d(r)
                    ()
                )
            } :> Task

        use cts = new CancellationTokenSource()
        let server = WebServer.run 4322 cts.Token gotImage
    

        let index = cval 0
        win.Keyboard.KeyDown(Keys.L).Values.Add(fun _ -> 
            transact (fun _ -> 
                index.Value <- (index.Value + 1) % 2
                Log.line "%A" index.Value
            )
        ) |> ignore

        let fullScreen = 
            Sg.fullScreenQuad 
            |> Sg.texture "LeftTexture" left
            |> Sg.texture "RightTexture" right
            |> Sg.uniform "Index" index
            |> Sg.shader {
                do! Shader.applyLeftRight
                //do! Shader.test
            }

        // assign the render task to our window...
        win.RenderTask <- win.Runtime.CompileRender(win.FramebufferSignature, fullScreen)
        win.Run()
        0