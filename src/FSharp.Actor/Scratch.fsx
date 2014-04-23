#load "FSharp.Actor.fsx"

open FSharp.Actor

let system = ActorSystem.Create("world", onError = (fun err -> err.Sender <-- Restart))
system.ReportEvents()

type Weapon = 
    | Cannon
    | Missile
    with 
        member x.Damage
            with get() = 
                match x with
                | Cannon -> 4
                | Missile -> 10

type SpaceshipAction = 
    | MoveLeft 
    | MoveRight
    | MoveUp 
    | MoveDown
    | Fire of Weapon
    | TakeHit of Weapon

type Spaceship = {
    PositionX : int
    PositionY : int
    Arsenal : Map<Weapon, int>
    Armour : int
    Shield : int
}
with
    static member Empty =
        {
            PositionX = 0; PositionY = 0; 
            Arsenal = Map [Cannon, 500;  Missile, 3] 
            Armour = 100
            Shield = 100
        }
    member x.IsDestroyed = x.Armour <= 0

type RendererAction =
    | UpdateShip of Spaceship

let renderer = 
    actor { 
        path "renderer"
        messageHandler (fun actor -> 
            let rec loop() = async {
                let! msg = actor.Receive()
                match msg.Message with
                | UpdateShip(ship) -> actor.Logger.DebugFormat(fun r -> r "ship location X:%d Y:%d Armour:%d Shield:%d" ship.PositionX ship.PositionY ship.Armour ship.Shield)
                return! loop()
            }
            loop()
        )
    } |> system.SpawnActor

let spaceship = 
    actor {
        path "spaceship"
        messageHandler (fun actor ->
            let renderer = !!"renderer"
            let rec loop (state:Spaceship) = async {
                let! msg = actor.Receive()
                let state =
                    match msg.Message with
                    | MoveLeft ->  
                        { state with PositionX = (max 0 (state.PositionX - 1))}
                    | MoveRight ->  
                        { state with PositionX = (max 0 (state.PositionX + 1))}
                    | MoveUp ->  
                        { state with PositionY = (max 0 (state.PositionY + 1))}
                    | MoveDown ->  
                        { state with PositionY = (max 0 (state.PositionY - 1))}
                    | Fire weapon -> 
                        match state.Arsenal.[weapon] with
                        | 0 -> state
                        | count -> { state with Arsenal = Map.add weapon (count - 1) state.Arsenal }
                    | TakeHit weapon ->
                        if state.Shield = 0
                        then { state with Armour = max 0 (state.Armour - weapon.Damage) }
                        else { state with Shield = max 0 (state.Shield - weapon.Damage) }

                if state.IsDestroyed
                then failwithf "Ship destroyed"
                else renderer <-- UpdateShip state
                return! loop state
            }
            loop Spaceship.Empty)
    } |> system.SpawnActor

let rnd = new System.Random()
let cts = new System.Threading.CancellationTokenSource()
let rec gameLoop() = async {
    let action =
        match rnd.Next(0, 7) with
        | 0 -> MoveLeft 
        | 1 -> MoveRight
        | 2 -> MoveUp 
        | 3 -> MoveDown
        | 4 -> Fire Missile
        | 5 -> Fire Cannon
        | 6 -> TakeHit Cannon
        | 7 -> TakeHit Missile
        | _ -> failwithf "WTF!!"
    spaceship <-- action
    do! Async.Sleep(750)
    return! gameLoop()
}

let start() = 
    Async.Start(gameLoop(), cts.Token)

let stop() = 
    cts.Cancel()



