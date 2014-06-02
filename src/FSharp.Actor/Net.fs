namespace FSharp.Actor

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation
open System.Collections.Concurrent
open System.Threading
open Microsoft.FSharp.Control
open Nessos.FsPickler

module internal Net =


    let getIPAddress() = 
        if NetworkInterface.GetIsNetworkAvailable()
        then 
            let host = Dns.GetHostEntry(Dns.GetHostName())
            host.AddressList
            |> Seq.find (fun add -> add.AddressFamily = AddressFamily.InterNetwork)
        else IPAddress.Loopback
    
    let getFirstFreePort() = 
        let defaultPort = 8080
        let usedports = 
            IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners() 
            |> Seq.map (fun x -> x.Port)
        
        let ports = 
            seq { 
                for port in defaultPort..defaultPort + 2048 do
                    yield port
            }
        
        let port = ports |> Seq.find (fun p -> Seq.forall ((<>) p) usedports)
        port
    
    let availableEndpoint() =
        new IPEndPoint(getIPAddress(), getFirstFreePort())

[<CustomComparison; CustomEquality>]
type NetAddress = 
    | NetAddress of IPEndPoint
    override x.Equals(y:obj) =
        match y with
        | :? IPEndPoint as ip -> ip.ToString().Equals(x.ToString())
        | :? NetAddress as add -> 
            match add with
            | NetAddress(ip) ->  ip.ToString().Equals(x.ToString())
        | _ -> false
    member x.Endpoint 
        with get() = 
            match x with
            | NetAddress(ip) -> ip
    override x.GetHashCode() = 
        match x with
        | NetAddress(ip) -> ip.GetHashCode()
    static member OfEndPoint(ip:EndPoint) = NetAddress(ip :?> IPEndPoint)
    interface IComparable with
        member x.CompareTo(y:obj) =
            match y with
            | :? IPEndPoint as ip -> ip.ToString().CompareTo(x.ToString())
            | :? NetAddress as add -> 
                match add with
                | NetAddress(ip) ->  ip.ToString().CompareTo(x.ToString())
            | _ -> -1



type UdpConfig<'a> = {
    Id : Guid
    MulticastPort : int
    MulticastGroup : IPAddress
    Heartbeat : (int * (unit -> 'a)) option
    Serialiser : ('a -> byte[])
    Deserialiser : (byte[] -> 'a)
}
with
    static member Default<'a>(?id, ?port, ?group, ?heartbeat, ?serialiser, ?deserialiser) : UdpConfig<'a> = 
        let pickler = new FsPickler()
        {
            Id = defaultArg id (Guid.NewGuid())
            MulticastPort = defaultArg port 2222
            MulticastGroup = defaultArg group (IPAddress.Parse("239.0.0.222"))
            Heartbeat = heartbeat
            Serialiser = defaultArg serialiser (pickler.Pickle)
            Deserialiser = defaultArg deserialiser (pickler.UnPickle)
        }
    member x.RemoteEndpoint = new IPEndPoint(x.MulticastGroup, x.MulticastPort)

type UDP<'a>(?config:UdpConfig<'a>) =       
    let msgReceived = new Event<IPEndPoint * 'a>()
    let mutable isStarted = false
    let config = defaultArg config (UdpConfig<'a>.Default())
    let clients = new ConcurrentDictionary<Guid, unit>()

    let publisher =
        lazy
            let client = new UdpClient()
            client.JoinMulticastGroup(config.MulticastGroup)
            client

    let listener =
        lazy
            let client = new UdpClient()
            client.ExclusiveAddressUse <- false
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true)
            client.Client.Bind(new IPEndPoint(IPAddress.Any, config.MulticastPort))
            client.JoinMulticastGroup(config.MulticastGroup)
            client

    let rec messageHandler() = async {
            let! received = listener.Value.ReceiveAsync() |> Async.AwaitTask
            if received.Buffer.Length > 16 
            then 
                let guid = new Guid(received.Buffer.[0..15])
                if guid <> config.Id && (not <| clients.ContainsKey guid)
                then 
                    clients.TryAdd(guid, ()) |> ignore
                    let payload = config.Deserialiser received.Buffer.[16..]
                    msgReceived.Trigger (received.RemoteEndPoint, payload)
                
            return! messageHandler()
        }

    let publish payload = async {
            let bytes = Array.append (config.Id.ToByteArray()) (config.Serialiser payload)
            let! bytesSent = publisher.Value.SendAsync(bytes, bytes.Length, config.RemoteEndpoint) |> Async.AwaitTask
            return()
        }

    let rec heartBeat interval payloadF = async {
        do! publish (payloadF()) 
        do! Async.Sleep(interval)
        return! heartBeat interval payloadF
    }

    member x.MessageRecieved = msgReceived.Publish
    
    member x.Publish (payload:'a) = 
        publish payload |> Async.RunSynchronously
        
    member x.Start ct = 
        if not(isStarted)
        then 
            Async.Start(messageHandler(), ct)
            match config.Heartbeat with
            | Some(interval, f) ->
                Async.Start(heartBeat interval f, ct)
            | None -> ()
            isStarted <- true

type Pool<'a>(size:int, ctor: (unit -> 'a)) = 
    let argsPool = new BlockingCollection<'a>(size)

    do
      for i in 1 .. size do
            argsPool.Add (ctor())  

    member a.CheckOut(timeout:int) = 
         let result = ref (Unchecked.defaultof<_>)
         if argsPool.TryTake(result, timeout)
         then !result
         else raise(TimeoutException())

    member a.CheckIn(args) = argsPool.Add(args)

    type TcpConfig<'a> = {
        Id : string
        ListenerEndpoint : IPEndPoint
        Backlog : int
        Serialiser : ('a -> byte[])
        Deserialiser : (byte[] -> 'a)
    }
    with
        static member Default<'a>(listener, ?id, ?serialiser, ?deserialiser) : TcpConfig<'a> = 
            let pickler = new FsPickler()
            {
                Id = defaultArg id (Guid.NewGuid().ToString())
                ListenerEndpoint = listener
                Backlog = 10000
                Serialiser = defaultArg serialiser (pickler.Pickle)
                Deserialiser = defaultArg deserialiser (pickler.UnPickle)
            }
        static member Default<'a>(?port, ?id, ?serialiser, ?deserialiser) : TcpConfig<'a> = 
            let pickler = new FsPickler()
            {
                Id = defaultArg id (Guid.NewGuid().ToString())
                ListenerEndpoint = (new IPEndPoint(Net.getIPAddress(), defaultArg port (Net.getFirstFreePort())))
                Backlog = 10000
                Serialiser = defaultArg serialiser (pickler.Pickle)
                Deserialiser = defaultArg deserialiser (pickler.UnPickle)
            }

    type TCP<'a>(config:TcpConfig<'a>) =        
        let received = new Event<NetAddress * 'a>()
        let mutable isStarted = false
        let listener =
            lazy
                let l = new TcpListener(config.ListenerEndpoint)
                l.Start(config.Backlog)
                l

        let rec messageHandler() = async {
                let! client = listener.Value.AcceptTcpClientAsync() |> Async.AwaitTask
                use client = client
                use stream = client.GetStream()
                let! (message:byte[]) = stream.ReadBytesAsync()
                received.Trigger <| ((NetAddress.OfEndPoint client.Client.RemoteEndPoint),config.Deserialiser message)
                return! messageHandler()
            }

        let publishAsync (endpoint:IPEndPoint) (payload:'a) = async {
                use client = new TcpClient()
                client.Connect(endpoint)
                use stream  = client.GetStream()
                do! stream.WriteBytesAsync(config.Serialiser payload)
                do! stream.FlushAsync().ContinueWith(ignore) |> Async.AwaitTask
            }
        
        member x.Recieved = received.Publish

        member x.Publish(endpoint, payload) = 
            publishAsync endpoint payload |> Async.RunSynchronously

        member x.PublishAsync(endpoint, payload) = 
            publishAsync endpoint payload

        member x.Start(ct) =
            if not isStarted
            then 
                Async.Start(messageHandler(), ct)