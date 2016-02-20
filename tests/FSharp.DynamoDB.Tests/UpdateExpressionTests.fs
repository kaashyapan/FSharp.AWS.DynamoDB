﻿namespace FSharp.DynamoDB.Tests

open System
open System.Threading

open Xunit
open FsUnit.Xunit

open FSharp.DynamoDB

[<AutoOpen>]
module UpdateExprTypes =

    type Enum = A = 0 | B = 1 | C = 2

    type Nested = { NV : string ; NE : Enum }

    type UpdateExprRecord =
        {
            [<HashKey>]
            HashKey : string
            [<RangeKey>]
            RangeKey : string

            Value : int64

            String : string

            Tuple : int64 * int64

            Nested : Nested

            NestedList : Nested list

            TimeSpan : TimeSpan

            DateTimeOffset : DateTimeOffset

            Guid : Guid

            Bool : bool

            Bytes : byte[]

            Ref : string ref

            Optional : string option

            List : int64 list

            Map : Map<string, int64>

            Set : Set<int64>

            [<BinaryFormatter>]
            Serialized : int64 * string
        }

type ``Update Expression Tests`` () =

    let client = getDynamoDBAccount()
    let tableName = getRandomTableName()

    let rand = let r = Random() in fun () -> int64 <| r.Next()
    let mkItem() = 
        { 
            HashKey = guid() ; RangeKey = guid() ; String = guid()
            Value = rand() ; Tuple = rand(), rand() ;
            TimeSpan = TimeSpan.FromTicks(rand()) ; DateTimeOffset = DateTimeOffset.Now ; Guid = Guid.NewGuid()
            Bool = false ; Optional = Some (guid()) ; Ref = ref (guid()) ; Bytes = Guid.NewGuid().ToByteArray()
            Nested = { NV = guid() ; NE = enum<Enum> (int (rand()) % 3) } ;
            NestedList = [{ NV = guid() ; NE = enum<Enum> (int (rand()) % 3) } ]
            Map = seq { for i in 0L .. rand() % 5L -> "K" + guid(), rand() } |> Map.ofSeq 
            Set = seq { for i in 0L .. rand() % 5L -> rand() } |> Set.ofSeq
            List = [for i in 0L .. rand() % 5L -> rand() ]
            Serialized = rand(), guid()
        }

    let run = Async.RunSynchronously

    let table = TableContext.GetTableContext<UpdateExprRecord>(client, tableName, createIfNotExists = true) |> run

    [<Fact>]
    let ``Attempt to update HashKey`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        fun () -> table.UpdateItemAsync(key, <@ fun r -> { r with HashKey = guid() } @>) |> run
        |> shouldFailwith<_, ArgumentException>

    [<Fact>]
    let ``Attempt to update RangeKey`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        fun () -> table.UpdateItemAsync(key, <@ fun r -> { r with RangeKey = guid() } @>) |> run
        |> shouldFailwith<_, ArgumentException>

    [<Fact>]
    let ``Simple update DateTimeOffset`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let nv = DateTimeOffset.Now + TimeSpan.FromDays 366.
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with DateTimeOffset = nv } @>) |> run
        item'.DateTimeOffset |> should equal nv

    [<Fact>]
    let ``Simple update TimeSpan`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let ts = TimeSpan.FromTicks(rand())
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with TimeSpan = ts } @>) |> run
        item'.TimeSpan |> should equal ts

    [<Fact>]
    let ``Simple update Guid`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let g = Guid.NewGuid()
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Guid = g } @>) |> run
        item'.Guid |> should equal g

    [<Fact>]
    let ``Simple increment update`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Value = r.Value + 1L } @>) |> run
        item'.Value |> should equal (item.Value + 1L)

    [<Fact>]
    let ``Simple decrement update`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Value = r.Value - 10L } @>) |> run
        item'.Value |> should equal (item.Value - 10L)

    [<Fact>]
    let ``Simple update serialized value`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let value' = rand(), guid()
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Serialized = value' } @>) |> run
        item'.Serialized |> should equal value'

    [<Fact>]
    let ``Update using nested record values`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with String = r.Nested.NV } @>) |> run
        item'.String |> should equal item.Nested.NV

    [<Fact>]
    let ``Update using nested list`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Nested = r.NestedList.[0] } @>) |> run
        item'.Nested |> should equal item.NestedList.[0]

    [<Fact>]
    let ``Update using tuple values`` () =
        let item = mkItem()
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Value = fst r.Tuple + 1L } @>) |> run
        item'.Value |> should equal (fst item.Tuple + 1L)

    [<Fact>]
    let ``Update optional field to None`` () =
        let item = { mkItem() with Optional = Some (guid()) }
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Optional = None } @>) |> run
        item'.Optional |> should equal None

    [<Fact>]
    let ``Update optional field to Some`` () =
        let item = { mkItem() with Optional = None }
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with Optional = Some(guid()) } @>) |> run
        item'.Optional.IsSome |> should equal true

    [<Fact>]
    let ``Update list field to non-empty`` () =
        let item = { mkItem() with List = [1L] }
        let key = table.PutItemAsync item |> run
        let nv = [for i in 1 .. 10 -> rand() ]
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with List = nv } @>) |> run
        item'.List |> should equal nv

    [<Fact>]
    let ``Update list field to empty`` () =
        let item = { mkItem() with List = [1L] }
        let key = table.PutItemAsync item |> run
        let item' = table.UpdateItemAsync(key, <@ fun r -> { r with List = [] } @>) |> run
        item'.List.Length |> should equal 0