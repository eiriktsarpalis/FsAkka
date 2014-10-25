#I "../bin/"

#r "Newtonsoft.Json.dll"
#r "Akka.dll"

open Akka
open Akka.Actor
open Akka.Configuration
open System

//let config = 
//    FluentConfig
//        .Begin()
//        .StartRemotingOn("localhost", 4567)
//        .StdOutLogLevel(Event.LogLevel.ErrorLevel)
//        .Build()
//
//let system = ActorSystem.Create("actors", config)
//
//let testActor =
//    Props.Stateful 0 (fun self state (msg : ReplyChannel<int>) -> msg.Reply self state ; state + 1)
//    |> Actor.start system "test"
//
//testActor.Uri