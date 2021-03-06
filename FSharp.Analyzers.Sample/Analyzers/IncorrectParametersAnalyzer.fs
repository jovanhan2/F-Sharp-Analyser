module IncorrectParameters

open FSharp.Analyzers.SDK
// open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.Range
open Microsoft.FSharp.Compiler.Ast
open Microsoft.FSharp.Compiler.SourceCodeServices
open Microsoft.FSharp.Compiler.Range
open System.IO

let mutable functionNames = Map.empty

// let rec countArg funcExpr (count: int byref) = 
//     match funcExpr with 
//     | SynExpr.App(exprAtomicFlag, isInfix, funcExpr, argExpr, m) -> 
//         printfn "In COUNT with funcExpr %A | argExpr %A" funcExpr argExpr
//         count <- count +  1
//         printfn "count %d" count
//         countArg funcExpr &count
//         printfn "count FuncExpr %d" count
//         countArg argExpr &count
//         printfn "count Arg %d" count
//     | SynExpr.Const(c,range) ->
//         count <- count + 1
//     | SynExpr.Ident(name)->
//         count <- count + 1
//     | x -> printfn "COUNT UNMATCHED %A" x
let visitSynVal (x:SynValData) =
    match x with 
    | SynValData (memberFlags,synvalInfo,indentOption) ->
        // printfn "memberFlags %A" memberFlags
        // printfn "SynvalInfo %A" synvalInfo
        match synvalInfo with
        | SynValInfo (curriedArgsInfo, returninfo) ->
            // printfn "curriedArgsInfo"  
            // printfn "There are %d args" curriedArgsInfo.Length
            // for arg in curriedArgsInfo do
            //     printfn "args %A" arg
            curriedArgsInfo.Length
            // printfn "returnInfo %A" returninfo
        // printfn "indentOption %A" indentOption
    
let rec visitPattern pat data = 
    match pat with
    | SynPat.Wild(x) -> () 
    // printfn "  .. underscore pattern"
    | SynPat.Named(pat, name, _, _, _) ->   
        visitPattern pat data
        // printfn "  .. named as '%s'" name.idText
        let numArgs = visitSynVal data
        functionNames <- functionNames.Add(name.idText,numArgs)
        // This is for let result =.. -> result
    | SynPat.LongIdent(LongIdentWithDots(ident, _), _, _, _, _, _) -> 
        let names = 
            String.concat "." [for i in ident -> i.idText]
        // printfn "  .. identifier: %s" names
        // identifier is name of function call 
        // let add x y =... -> add
        let numArgs = visitSynVal data
        functionNames <- functionNames.Add(names,numArgs)
        ()
    | _ -> ()

let rec visitExpression handler body= 
    match body with
    | SynExpr.Paren(expr,leftParenRange,rightParenRange,range) ->
        visitExpression handler expr
    | SynExpr.App(exprAtomicFlag, isInfix, funcExpr, argExpr, range) -> 
        // printfn "In APP with funcExpr %A | argExpr %A" funcExpr argExpr
        match funcExpr with 
        | SynExpr.Ident(name) -> 
        // printfn "name of function call: %A" name
        let containsKey = (Map.containsKey (name.ToString()) functionNames)
        // printfn "Checking %A exists with function names: %A " name containsKey
        if containsKey then
            // let mutable count = 0
            // countArg body &count  
            let res = (functionNames.TryFind (name.ToString()))
            let mutable numArgs = 0
            match res with
                | Some y -> (numArgs <- y)
                | None -> ()
            // printfn "num args %d" numArgs
            handler range (name.ToString()) numArgs
        | _ -> ()
        visitExpression handler funcExpr
        visitExpression handler argExpr
    | SynExpr.LongIdent(optional,name,refcall,range) -> 
        let containsKey = (Map.containsKey (name.ToString()) functionNames)
        // printfn "Checking %A exists with function names: %A " name containsKey
        if containsKey then
            // let mutable count = 0
            // countArg body &count  
            let res = (functionNames.TryFind (name.ToString()))
            let mutable numArgs = 0
            match res with
                | Some y -> (numArgs <- y)
                | None -> ()
            // printfn "num args %d" numArgs
            handler range (name.ToString()) numArgs
    | SynExpr.IfThenElse(cond, trueBranch, falseBranchOpt, _, _, _, _) ->
      // Visit all sub-expressions
      printfn "Conditional:"
      visitExpression handler cond
      visitExpression handler trueBranch
      falseBranchOpt |> Option.iter (visitExpression handler) 
    | SynExpr.LetOrUse(isRecurisve,isUse,bindings,body,range) ->
        for binding in bindings do
            let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, 
                         data, pat, retInfo, init, m, sp)) = binding
            visitPattern pat data
            visitExpression handler init
          // Visit the body expression
        printfn "And the following body:"
        visitExpression handler body
                
    | x -> ()
    // printfn "unmatched! %A " x
       
    // | pat -> printfn "  .. other pattern: %A" pat



let visitDeclarations handler decls = 
    for declaration in decls do
        match declaration with
        | SynModuleDecl.Let(isRec, bindings, range) -> 
            for binding in bindings do
                let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, data, 
                             pat, retInfo, body, m, sp)) = binding
                // printfn "################# \n"
                // visitSynVal data
                // // printfn "SynVal data : %A" data
                // visitPattern pat \
                visitPattern pat data
                visitExpression handler body
                // printfn "################# \n"
        | _ -> ()

let visitModulesAndNamespaces handler modulesOrNss = 
    let (SynModuleOrNamespace(lid, isRec, isMod, decls, xml, attrs, _, m)) = 
        modulesOrNss
    // printfn "Namespace or module: %A" lid
    visitDeclarations handler decls

let checker = FSharpChecker.Create(keepAssemblyContents=true)
let parseAndCheckSingleFile (input) = 
    let file = Path.ChangeExtension(System.IO.Path.GetTempFileName(), "fsx")  
    File.WriteAllText(file, input)
    // Get context representing a stand-alone (script) file
    let projOptions, _errors = 
        checker.GetProjectOptionsFromScript(file, input)
        |> Async.RunSynchronously

    let fprojOptions = projOptions

    checker.ParseAndCheckProject (fprojOptions)
    |> Async.RunSynchronously

[<Analyzer>]
let IncorrectParameters : Analyzer  =
    // printfn "IncorrectParameter Analyser"
    functionNames <- Map.empty
    fun ctx ->
        // printfn "ctx %A" ctx.ParseTree
        let state = ResizeArray<range>()
        let string = ctx.Content |> String.concat "\n"
        let checkProjectResults = parseAndCheckSingleFile(string)
        // printfn "Errors: %A" checkProjectResults.Errors
        let mutable FunctionName = ""
        let mutable ExpectedArguments = 0
        if checkProjectResults.Errors.Length > 0 then
            // handler adds the range to display
            let handler (range: range) functionName expectedArguments = 
                // printfn "###################################"
                // printfn "SynExpr type %A" m
                // printfn "###################################"
                FunctionName <- functionName
                ExpectedArguments <- expectedArguments
                state.Add range
            let parseTree = ctx.ParseTree
            match parseTree with
            | ParsedInput.ImplFile(implFile) ->
                // Extract declarations and walk over them
                let (ParsedImplFileInput(fn, script, name, _, _, modules, _)) = implFile
                modules |>  List.iter (visitModulesAndNamespaces handler)
            | _ -> failwith "F# Interface file (*.fsi) not supported."
            // printfn "functionNames %A" functionNames

        state
        |> Seq.map (fun r ->
            { Type = "Possibly wrong number of parameters"
              Message = "For function " + FunctionName + ", which expects "+ ExpectedArguments.ToString() + " arguments "
              Code = "P001"
              Severity = Warning
              Range = r
              Fixes = []}

        )
        |> Seq.toList