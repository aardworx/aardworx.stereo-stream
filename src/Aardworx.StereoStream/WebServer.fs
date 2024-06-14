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

    type StereoImage = { left : MemoryStream; right : MemoryStream; size : V2i }

    let receiveFull (ws : WebSocket) (ct : CancellationToken) =
        let buffer = Array.zeroCreate 32768 
        let rec run (data : MemoryStream) = 
            task {
                let s = ArraySegment(buffer)
                let! r = ws.ReceiveAsync(s, ct)
                if r.CloseStatus.HasValue  then
                    return None
                else
                    data.Write(buffer, 0, r.Count)
                    if r.EndOfMessage then
                        return Some data
                    else
                        return! run data
            }
        task {
            let mem = new MemoryStream()
            match! run mem with
            | None -> return None
            | Some r -> 
                r.Position <- 0L
                return r |> Some

        }
        

    let render (receivedImage : StereoImage -> Task)  (ws : WebSocket) (context : HttpContext) : Task<unit> = 
        task {
            let mutable running = true
            let sw = System.Diagnostics.Stopwatch.StartNew()
            while running do
                let buffer = Array.zeroCreate 1024 
                let! r = ws.ReceiveAsync(ArraySegment(buffer), context.RequestAborted)
                let data = Array.sub buffer 0 r.Count
                if r.CloseStatus.HasValue  then
                    Log.warn "closed:%A" r.CloseStatus
                    running <- false
                else
                    match r.MessageType with
                    | WebSocketMessageType.Text -> 
                        let str = System.Text.Encoding.UTF8.GetString(data, 0, data.Length)
                        match str.Split(";") with
                        | [| w; h |] -> 
                            Log.warn "size: %A %A" w h
                            let! left = receiveFull ws context.RequestAborted
                            let! right = receiveFull ws context.RequestAborted
                            Log.line "fps:%A" (1000.0 / float sw.Elapsed.TotalMilliseconds)
                            sw.Restart()
                            match left, right with
                            | Some left, Some right -> 
                                do! receivedImage { left = left; right = right; size = V2i(int w,int h) }
                                left.Dispose()
                                right.Dispose()
                                let b = System.Text.Encoding.UTF8.GetBytes("ok")
                                do! ws.SendAsync(ArraySegment(b), WebSocketMessageType.Text, true, context.RequestAborted)
                            | _ -> 
                                Log.warn "protocol error 2"
                                running <- false
                        | _ -> 
                            Log.warn "protocol error 3"
                            running <- false
                    | _ -> 
                        Log.warn "not text"

        }

    let configureApp webApp (app : IApplicationBuilder) =
        app.UseWebSockets().UseGiraffe webApp

    let configureServices (services : IServiceCollection) =
        services.AddGiraffe() |> ignore

    let configureLogging (builder : ILoggingBuilder) =
        let filter (l : LogLevel) = l.Equals LogLevel.Information
        builder.AddFilter(filter)
                .AddConsole()
                .AddDebug()
        |> ignore


    let run (port : int) (ct : CancellationToken) (gotImage : StereoImage -> Task) =
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
                          .ConfigureLogging(configureLogging)
                          .UseUrls(url)
                          |> ignore)
             .Build()
             .StartAsync(ct)