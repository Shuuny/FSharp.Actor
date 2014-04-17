namespace FSharp.Actor

module Net = 
    
    open System
    open System.Net
    open System.Net.Sockets
    open System.Net.NetworkInformation
    open System.Collections.Concurrent
    open System.Threading
//    open Nessos.FsPickler
//
//    type UdpConfig = {
//        Id : Guid
//        MulticastPort : int
//        MulticastGroup : IPAddress
//    }
//    with
//        static member Default(?id, ?interval, ?port, ?group) = {
//            Id = defaultArg id (Guid.NewGuid())
//            MulticastPort = defaultArg port 2222
//            MulticastGroup = defaultArg group (IPAddress.Parse("239.0.0.222"))
//        }
//        member x.RemoteEndpoint = new IPEndPoint(x.MulticastGroup, x.MulticastPort)
//
//    type UDP<'a>(?config:UdpConfig) =       
//        let msgReceived = new Event<IPEndPoint * 'a>()
//        let mutable isStarted = false
//        let config = defaultArg config (UdpConfig.Default())
//        let pickler = new FsPickler()
//
//        let publisher =
//            lazy
//                let client = new UdpClient()
//                client.JoinMulticastGroup(config.MulticastGroup)
//                client
//
//        let listener =
//            lazy
//                let client = new UdpClient()
//                client.ExclusiveAddressUse <- false
//                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true)
//                client.Client.Bind(new IPEndPoint(IPAddress.Any, config.MulticastPort))
//                client.JoinMulticastGroup(config.MulticastGroup)
//                client
//    
//        let rec messageHandler() = async {
//                let! received = listener.Value.ReceiveAsync() |> Async.AwaitTask
//                if received.Buffer.Length > 16 
//                then 
//                    let guid = new Guid(received.Buffer.[0..15])
//                    if guid <> config.Id
//                    then 
//                        let payload = pickler.UnPickle received.Buffer.[16..]
//                        msgReceived.Trigger (received.RemoteEndPoint, payload)
//                return! messageHandler()
//            }
//
//        member x.MessageRecieved = msgReceived.Publish
//        
//        member x.Publish (payload:'a) = 
//            let bytes = Array.append (config.Id.ToByteArray()) (pickler.Pickle payload)
//            publisher.Value.Send(bytes, bytes.Length, config.RemoteEndpoint)
//
//        member x.Start ct = 
//            if not(isStarted)
//            then 
//                Async.Start(messageHandler(), ct)
//                isStarted <- true
    
    let IPAddress = 
        lazy
            if NetworkInterface.GetIsNetworkAvailable()
            then 
                let host = Dns.GetHostEntry(Dns.GetHostName())
                host.AddressList
                |> Seq.tryFind (fun add -> add.AddressFamily = AddressFamily.InterNetwork)
            else None
    
    let FirstFreePort = 
        lazy
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

