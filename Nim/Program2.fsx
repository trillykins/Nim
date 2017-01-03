﻿// Code from Hansen and Rischel: Functional Programming using F#     1       6/12 2012
// Chapter 13: Asynchronous and parallel computations.          Revised MRH 25/11 2013 
// Code from Section 13.5: 13.5 Reactive programs.


// Prelude
open System 
open System.Net 
open System.Threading 
open System.Windows.Forms 
open System.Drawing 
open System.Text.RegularExpressions

System.IO.Directory.SetCurrentDirectory __SOURCE_DIRECTORY__;;

#r @"AsyncEventQueue.dll"
open AsyncEventQueue

type Heap = int list

// The window part
let window = new Form(Text="Nim", Size=Size(500, 500))

let easyButton =  new Button(Location=Point(50,65),MinimumSize=Size(100,50), MaximumSize=Size(100,50),Text="EASY")

let hardButton = new Button(Location=Point(200,65),MinimumSize=Size(100,50), MaximumSize=Size(100,50),Text="HARD")

let clearButton = new Button(Location=Point(350,65),MinimumSize=Size(100,50), MaximumSize=Size(100,50),Text="CLEAR")

let endTurnButton = new Button(Location=Point(750,65),MinimumSize=Size(100,50), MaximumSize=Size(100,50),Text="END TURN")

let cancelButton = new Button(Location=Point(500,65),MinimumSize=Size(100,50), MaximumSize=Size(100,50),Text="ABORT")

let combo = new ComboBox(Location=Point(100,35), DataSource=[|"http://www2.compute.dtu.dk/~mire/02257/nim1.game";"http://www2.compute.dtu.dk/~mire/02257/nim2.game";"http://www2.compute.dtu.dk/~mire/02257/nim3.game";"http://www2.compute.dtu.dk/~mire/02257/nim4.game"|], Width=300)

let mutable buttons = Array.empty
let mutable matches = Array.empty

let addMatches (arr:int list) = 
    Seq.toArray(seq{
        for i in 1..arr.Length do
            for x in 1..arr.[i-1] do
                yield new PictureBox(Image=Image.FromFile("hatteland2.png"), Top=(i*50+75), Left=(1250-(x*10)), Width=5)
    } |> Seq.cast<Control>)

let addButtons (arr:int list) = 
    Seq.toArray(seq{ 
        for y in 1..arr.Length do
            yield new Button(Text="-", Top=(y*50+100), Left=1300, Size=Size(20,20), BackColor=Color.Aqua)
    } |> Seq.cast<Control>)

let getOptimal arr =
    let calc_m arr = Array.fold (fun x m -> x ^^^ m) 0 arr
    let maxIndex arr = Array.findIndex (fun x -> x = Array.max arr) arr
    let m = calc_m arr
    if m <> 0 then
        for i = 0 to arr.Length-1 do
            let tmp = arr.[i] ^^^ m
            if tmp < arr.[i] then
                arr.[i] <- tmp
    else
        let maxI = maxIndex arr
        arr.[maxI] <- arr.[maxI]-1

let disable bs = 
    for b in [easyButton;clearButton;cancelButton;hardButton;endTurnButton] do 
        b.Enabled  <- true
    for (b:Button) in bs do 
        b.Enabled  <- false

// An enumeration of the possible events 
type Message = | Start of string * bool | Next | Clear | Cancel | Web of string | Error | Cancelled 

//exception UnexpectedMessage

// The dialogue automaton 
let ev = AsyncEventQueue()
let rec ready() = 
  async {
         Seq.iter(fun x -> window.Controls.Remove x) buttons
         Seq.iter(fun x -> window.Controls.Remove x) matches

         disable [cancelButton]
         endTurnButton.Visible <- false
         combo.Enabled <- true
         let! msg = ev.Receive()
         match msg with
         | Start (url, diff) -> return! loading(url)
         | Clear     -> return! ready()
         | _         -> failwith("ready: unexpected message")}
  
// Sets up the board from chosen url
and loading(url) =
  async {use ts = new CancellationTokenSource()
         combo.Enabled <- false
       
          // start the load
         Async.StartWithContinuations
             (async { let webCl = new WebClient()
                      let! html = webCl.AsyncDownloadString(Uri url)
                      return html },
              (fun html -> ev.Post (Web html)),
              (fun _ -> ev.Post Error),
              (fun _ -> ev.Post Cancelled),
              ts.Token)

         disable [easyButton; hardButton; clearButton;endTurnButton]
         endTurnButton.Visible <- true
         let! msg = ev.Receive()
         match msg with
         | Web html ->
             let arr = List.map (fun x -> if x <= 9 && x > 0 then x else 9) ([ for x in Regex("\d+").Matches(html) -> int x.Value ])
             
             buttons <- addButtons arr
             matches <- addMatches arr

             endTurnButton.Top <- (Array.last buttons).Top + 50
             endTurnButton.Left <- (Array.last buttons).Left - 50
             
             window.Controls.AddRange matches
             window.Controls.AddRange buttons
             

             let arr = List.toArray arr
             return! player(arr)
         | Cancel  -> ts.Cancel()
                      return! cancelling()
         | _       -> failwith("loading: unexpected message")}

and player(arr) =
  async {disable [easyButton;hardButton;clearButton;cancelButton]
         let! msg = ev.Receive()
         match msg with
         | Next -> return! ai(arr)
         | Cancelled | Error | Web  _ ->
                   return! finished("Cancelled")
         | _    ->  failwith("cancelling: unexpected message")}

and ai(arr) =
  async {
         disable [easyButton;hardButton;clearButton;cancelButton;endTurnButton]
         getOptimal arr

         let! msg = ev.Receive()
         match msg with
         | Cancelled | Error | Web  _ ->
                   return! finished("Cancelled")
         | _    ->  failwith("cancelling: unexpected message")}
and cancelling() =
  async {
         disable [easyButton;hardButton;clearButton;cancelButton;endTurnButton]
         let! msg = ev.Receive()
         match msg with
         | Cancelled | Error | Web  _ ->
                   return! finished("Cancelled")
         | _    ->  failwith("cancelling: unexpected message")}

and finished(s) =
  async {
         disable [easyButton;hardButton;cancelButton;endTurnButton]
         let! msg = ev.Receive()
         match msg with
         | Clear -> return! ready()
         | _     ->  failwith("finished: unexpected message")}

// Initialization
window.Controls.Add easyButton
window.Controls.Add hardButton
window.Controls.Add clearButton
window.Controls.Add cancelButton
window.Controls.Add endTurnButton
window.Controls.Add combo

easyButton.Click.Add (fun _ -> ev.Post (Start (combo.SelectedItem.ToString(), true)))
hardButton.Click.Add (fun _ -> ev.Post (Start (combo.SelectedItem.ToString(), false)))
clearButton.Click.Add (fun _ -> ev.Post Clear)
cancelButton.Click.Add (fun _ -> ev.Post Cancel)
endTurnButton.Click.Add (fun _ -> ev.Post Next)

// Start
Async.StartImmediate (ready())
window.Show()
window.WindowState <- FormWindowState.Maximized