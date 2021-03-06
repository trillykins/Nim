﻿open System 
open System.Net 
open System.Threading 
open System.Text.RegularExpressions

#r @"GUI.dll"
open GUI
#r @"AsyncEventQueue.dll"
open AsyncEventQueue
#load @"GameLogic.fs"
open GameLogic

System.IO.Directory.SetCurrentDirectory __SOURCE_DIRECTORY__;;

// The dialogue automaton 
let ev = AsyncEventQueue()
let mutable optimal = true

// ready : unit -> Async<unit>
let rec ready() = 
  async {
         GUI.clearBoard()
         GUI.disable [GUI.clearButton;GUI.cancelButton]         
         let! msg = ev.Receive()
         match msg with
         | Start (url, diff) -> return! loading(url, diff)
         | Clear     -> return! ready()
         | _         -> failwith("ready: unexpected message")
  }
// loading : string * bool -> Async<unit>
and loading(url, diff) =
  async {
         use ts = new CancellationTokenSource()
         GUI.toggleCombo false
         GUI.toggleAns false
         optimal <- diff
         GUI.disable [GUI.easyButton; GUI.hardButton; GUI.clearButton; GUI.endTurnButton]   
         
          // start the load
         Async.StartWithContinuations
             (async { let webCl = new WebClient()
                      let! html = webCl.AsyncDownloadString(Uri (if url = "Use url in box above" then GUI.ans.Text else url))
                      return html },
              (fun html -> ev.Post (Web html)),
              (fun _ -> ev.Post Error),
              (fun _ -> ev.Post Cancelled), ts.Token)
         
         let! msg = ev.Receive()
         match msg with
         | Web html ->
             let arr = Array.map (fun x -> if x <= 9 && x > 0 then x else 9) [| for x in Regex("\d+").Matches(html) -> int x.Value |]
             GUI.setupBoard ev arr
             return! player(arr, false)
         | Cancelled -> return! finished("cancelled")
         | Error -> return! finished("error")
         | _       -> failwith("loading: unexpected message")
  }
// player : Heap * bool -> Async<unit>
and player(arr, t) =
  async {
         GUI.disable [GUI.easyButton;GUI.hardButton;GUI.endTurnButton;GUI.cancelButton]
         GUI.toggleEndTurnBtn t
         GUI.updateBoard arr 
         if GameLogic.checkGameState arr then
            return! finished("lose")
         
         let! msg = ev.Receive()
         match msg with
         | PlayerTurn(i) -> return! turn(arr, i)
         | Next -> return! ai(arr)
         | Clear -> return! ready()
         | _    ->  failwith("cancelling: unexpected message")
  }
// turn : Heap * int -> Async<unit>
and turn(arr, i) =
  async {
         GUI.disable [GUI.easyButton;GUI.hardButton;GUI.clearButton;GUI.cancelButton]
         GUI.toggleButtons i
         let newArr = GameLogic.makeMove arr i
         if checkGameState newArr then
            GUI.updateBoard newArr
            return! finished("win")
         return! player(newArr, true)
  }
// ai : Heap -> Async<unit>
and ai(arr) =
  async {
         GUI.disable [GUI.easyButton;GUI.hardButton;GUI.clearButton;GUI.endTurnButton;GUI.cancelButton]
         do! Async.Sleep(rnd.Next(1, 5) * 500)  // simulate the AI thinking
         GUI.checkTease arr optimal
         GUI.updateBoard arr
         let newArr = if optimal then GameLogic.getOptimal arr else GameLogic.getRandom(rnd.Next(0, arr.Length), arr)
         GUI.toggleButtonsAi newArr
         return! player(newArr, false)
  }
// finished : string -> Async<unit>
and finished(s) =
  async {
         GUI.disable [GUI.easyButton;GUI.hardButton;GUI.endTurnButton;GUI.cancelButton]
         GUI.endofgameLabel s
         let! msg = ev.Receive()
         match msg with
         | Clear -> return! ready()
         | _     ->  failwith("finished: unexpected message")
  }

Async.StartImmediate (ready())
GUI.initGUI ev
GUI.window.Show()
