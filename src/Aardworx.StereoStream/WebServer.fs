namespace Aardworx.StereoStream

open System.Net
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks

open Giraffe
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Http

open Aardvark.Base


module  Websockets =

    
    let handShake (f : WebSocket -> HttpContext -> Task<unit>)  (next : HttpFunc) (context : HttpContext) =
        task {
            match context.WebSockets.IsWebSocketRequest with
            | true -> 
                let! webSocket = context.WebSockets.AcceptWebSocketAsync()
                let! _ = f webSocket context
                return! next context
            | _ -> 
                return failwith "no ws request"
        }


module WebServer = 

    open System
    open System.IO

    type StereoImage = { combined : MemoryStream;  size : V2i }

    let receiveFull (ws : WebSocket) (ct : CancellationToken) =
        let mem = new MemoryStream()
        let mutable remaining = true
        
        let buffer = Array.zeroCreate 8192 
        let s = ArraySegment(buffer)
        let sw = System.Diagnostics.Stopwatch.StartNew()
        while remaining do
            let r = ws.ReceiveAsync(s, ct).Result
            mem.Write(buffer, 0, r.Count)
            if r.EndOfMessage then remaining <- false
        sw.Stop()

        Some mem

    let receiveFullBase64 (ws : WebSocket) (ct : CancellationToken) =
        let sb = new System.Text.StringBuilder()
        let mutable remaining = true
        
        let buffer = Array.zeroCreate 8192 
        let s = ArraySegment(buffer)
        while remaining  do
            let r = ws.ReceiveAsync(s, ct).Result
            let s = System.Text.Encoding.UTF8.GetString(buffer, 0, r.Count)
            sb.Append(s) |> ignore
            if r.EndOfMessage then remaining <- false

        let a = sb.ToString().ToCharArray()
        let payLoadArr = Convert.FromBase64CharArray(a, 22, a.Length - 22)
        let mem = new MemoryStream()
        mem.Write(payLoadArr)
        mem.Position <- 0L
        Some mem

       
        

    let render (receivedImage : StereoImage -> unit)  (ws : WebSocket) (context : HttpContext) : Task<unit> = 
        task {
            let mutable running = true
            let sw = System.Diagnostics.Stopwatch.StartNew()
         
            while running do
                let buffer = Array.zeroCreate 1024 
                let r = ws.ReceiveAsync(ArraySegment(buffer), context.RequestAborted).Result
                let data = Array.sub buffer 0 r.Count
                if r.CloseStatus.HasValue  then
                    Log.warn "closed:%A" r.CloseStatus
                    running <- false
                else
                    match r.MessageType with
                    | WebSocketMessageType.Binary -> 
                        ()
                    | WebSocketMessageType.Text -> 
                        let str = System.Text.Encoding.UTF8.GetString(data, 0, data.Length)
                        match str.Split(";") with
                        | [| w; h |] -> 
                            let sw = System.Diagnostics.Stopwatch.StartNew()
                            let left = receiveFullBase64 ws context.RequestAborted
                            sw.Stop()
                            Log.line "took: %A" sw.Elapsed.TotalMilliseconds
                            match left with
                            |Some left -> 
                                do receivedImage { combined = left; size = V2i(int w,int h) }
                            | _ -> 
                                Log.warn "protocoll error 2"

                            let b = System.Text.Encoding.UTF8.GetBytes("ok")
                            do ws.SendAsync(ArraySegment(b), WebSocketMessageType.Text, true, context.RequestAborted).Wait()
                            //Log.startTimed "rec"
                            //let left = receiveFull ws context.RequestAborted
                            //let right = receiveFull ws context.RequestAborted
                            //Log.stop()
                            //Log.startTimed "proc"
                            //Log.line "fps:%A" (1000.0 / float sw.Elapsed.TotalMilliseconds)
                            //sw.Restart()
                            //match left, right with
                            //| Some left, Some right -> 
                            //    do receivedImage { left = left; right = right; size = V2i(int w,int h) }
                            //    left.Dispose()
                            //    right.Dispose()
                            //    let b = System.Text.Encoding.UTF8.GetBytes("ok")
                            //    do ws.SendAsync(ArraySegment(b), WebSocketMessageType.Text, true, context.RequestAborted).Wait()
                            //    Log.stop()
                            //| _ -> 
                            //    Log.warn "protocol error 2"
                            //    running <- false
                        | _ -> 
                            Log.warn "protocol error 3"
                            running <- false
                    | _ -> 
                        Log.warn "not text"

        }

    let configureApp webApp (app : IApplicationBuilder) =
        let o = WebSocketOptions()
      
        app.UseWebSockets(o).UseGiraffe webApp

    let configureServices (services : IServiceCollection) =
        services.AddGiraffe() |> ignore

    let configureLogging (builder : ILoggingBuilder) =
        let filter (l : LogLevel) = l.Equals LogLevel.Information
        builder.AddFilter(filter)
                .AddConsole()
                .AddDebug()
        |> ignore


    let run (port : int) (ct : CancellationToken) (gotImage : StereoImage -> unit) =
        let url = sprintf "http://%A:%d" IPAddress.Loopback port
        let webApp =
            choose [
                route "/test"  >=> text "hi"
                route "/render" >=> (Websockets.handShake (render gotImage))
            ]
        Host.CreateDefaultBuilder()
             .ConfigureWebHostDefaults(
                 fun webHostBuilder ->
                        webHostBuilder
                          .Configure(configureApp webApp)
                          .ConfigureServices(configureServices)
                          //.ConfigureLogging(configureLogging)
                          .UseUrls(url)
                          |> ignore)
             .Build()
             .StartAsync(ct)