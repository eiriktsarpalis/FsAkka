module FsAkka

open System
open System.Runtime.Serialization
open System.Linq.Expressions

open Akka
open Akka.Actor
open Akka.Configuration
open Akka.Serialization

// adapted straight from Akka.FSharp
[<AutoOpen>]
module private Linq =
    
    let (|Lambda|_|) (e : Expression) = 
        match e with
        | :? LambdaExpression as l -> Some(l.Parameters, l.Body)
        | _ -> None
    
    let (|Call|_|) (e : Expression) = 
        match e with
        | :? MethodCallExpression as c -> Some(c.Object, c.Method, c.Arguments)
        | _ -> None
    
    let (|Method|) (e : System.Reflection.MethodInfo) = e.Name
    
    let (|Invoke|_|) = 
        function 
        | Call(o, Method("Invoke"), _) -> Some o
        | _ -> None
    
    let (|Ar|) (p : System.Collections.ObjectModel.ReadOnlyCollection<Expression>) = Array.ofSeq p

/// typed actor props container
type Props<'T> = internal Props of Props

type private Reply<'T> = Choice<'T,exn>

/// Typed actor instance.
type Actor<'Msg> (reactF : Actor<'Msg> -> 'Msg -> unit) =
    inherit Akka.Actor.UntypedActor()

    member internal self.Reply (rc : ReplyChannel<'R>) (value : 'R) =
        self.Sender.Tell(Reply<'R>.Choice1Of2 value)

    member internal self.ReplyWithException (rc : ReplyChannel<'R>) (exn : exn) =
        self.Sender.Tell(Reply<'R>.Choice2Of2 exn)

    override self.OnReceive o =
        match o with
        | :? 'Msg as msg -> reactF self msg
        | _ -> failwith "unrecognized message."

and ActorUri<'T> internal (uri : string) =
    member __.Uri = uri
    member __.Reify (system : ExtendedActorSystem) = system.Provider.ResolveActorRef uri

/// Typed ActorRef instance.
and ActorRef<'Msg> internal (actor : ActorRef) =

    member __.Uri = new ActorUri<'Msg>(Serialization.SerializedActorPath actor)
   
    /// Post a message to recipient actor
    member __.Post (msg : 'Msg) = actor.Tell msg
    /// Post a message with asynchronous reply
    member __.PostWithAsyncReply(msgB : ReplyChannel<'R> -> 'Msg) = async {
        let replyTask = actor.Ask(msgB RC)
        let! reply = Async.AwaitTask replyTask
        return
            match reply :?> Choice<'R, exn> with
            | Choice1Of2 r -> r
            | Choice2Of2 e -> raise e
    }

    /// Post a message with asynchronous reply
    member __.PostWithReply(msgB : ReplyChannel<'R> -> 'Msg) = 
        __.PostWithAsyncReply msgB |> Async.RunSynchronously

/// Reply channel -- really just a witness, all work done by actor context.
and ReplyChannel<'R> = internal | RC
with
    member rc.Reply (self : Actor<'T>) (value : 'R) = self.Reply rc value
    member rc.ReplyWithException (self : Actor<'T>) (exn : exn) = self.ReplyWithException rc exn

let inline (<--) (ref : ActorRef<'T>) msg = ref.Post msg
let inline (<!-) (ref : ActorRef<'T>) msgB = ref.PostWithAsyncReply msgB
let inline (<!=) (ref : ActorRef<'T>) msgB = ref.PostWithReply msgB

type Props =

    static member FromFactory<'T>(factory : Expression<Func<Actor<'T>>>) : Props<'T> =
        match factory with
        | Lambda(_, Invoke(Call(null, Method "ToFSharpFunc", Ar [| Lambda(_, p) |]))) -> 
            let expr = Expression.Lambda(p, [||]) :?> System.Linq.Expressions.Expression<System.Func<Actor<'T>>>
            let props = Props.Create expr
            Props<'T>.Props props

        | _ -> failwith "Doesn't match"

    static member Stateful (init : 'State) (f : Actor<'Msg> -> 'State -> 'Msg -> 'State) : Props<'Msg> =
        let state = ref init
        let reactF self msg = state := f self !state msg
        Props.FromFactory(fun () -> new Actor<'Msg>(reactF))

    static member Stateless (f : Actor<'Msg> -> 'Msg -> unit) : Props<'Msg> =
        Props.FromFactory(fun () -> new Actor<'Msg>(f))

[<RequireQualifiedAccess>]
module Actor =

    let start (system : ActorSystem) (name : string) (Props props : Props<'T>) =
        let actorRef = system.ActorOf(props, name)
        new ActorRef<'T>(actorRef)