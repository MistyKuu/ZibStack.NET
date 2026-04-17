---
title: "TypeGen — GraphQL emitter"
description: "TypeTarget.GraphQL emits .graphql SDL files from C# DTOs"
---

`TypeTarget.GraphQL` emits [GraphQL SDL](https://graphql.org/learn/schema/)
`.graphql` files from C# DTOs — types, enums, and their relationships. Useful
when your C# models are the source of truth and you need a matching GraphQL
schema for a gateway, federation layer, or schema-first GraphQL server.

```csharp
[GenerateTypes(Targets = TypeTarget.GraphQL, OutputDir = "../schema")]
public class Order
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<LineItem> Lines { get; set; } = [];
    public string? Note { get; set; }
}

public enum OrderStatus
{
    Pending,
    Confirmed,
    Shipped,
    Cancelled
}

public class LineItem
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
```

```graphql
# schema.graphql

type Order {
  id: ID!
  customer: String!
  status: OrderStatus!
  createdAt: DateTime!
  lines: [LineItem!]!
  note: String
}

enum OrderStatus {
  PENDING
  CONFIRMED
  SHIPPED
  CANCELLED
}

type LineItem {
  productId: Int!
  quantity: Int!
  price: Float!
}
```

## Type mapping

| C# | GraphQL |
|---|---|
| `int`, `short`, `byte` | `Int` |
| `long` | `Int` *(GraphQL has no native long — use custom scalar if needed)* |
| `float`, `double`, `decimal` | `Float` |
| `string` | `String` |
| `bool` | `Boolean` |
| `Guid` | `ID` |
| `DateTime`, `DateTimeOffset` | `DateTime` *(custom scalar)* |
| `DateOnly` | `Date` *(custom scalar)* |
| `TimeOnly` | `Time` *(custom scalar)* |
| `T?` (nullable) | field without `!` |
| non-nullable | field with `!` |
| `List<T>`, `T[]`, `IEnumerable<T>` | `[T!]!` |
| `List<T>?` | `[T!]` |
| `Dictionary<string, V>` | `JSON` *(custom scalar — no native map in GraphQL)* |
| user DTO | named `type` reference |
| `enum` | `enum` with UPPER_SNAKE_CASE values |

## File layout

**SingleFile** (default) — all types emitted into one `.graphql` file:

```csharp
[GenerateTypes(Targets = TypeTarget.GraphQL, OutputDir = "../schema")]
public class Order { ... }
// → ../schema/schema.graphql (contains Order, OrderStatus, LineItem, etc.)
```

**FilePerType** — one `.graphql` file per type/enum:

```csharp
b.GraphQL(g =>
{
    g.FileLayout = GraphQLFileLayout.FilePerType;
    g.OutputDir = "../schema/types";
});
// → ../schema/types/Order.graphql
// → ../schema/types/OrderStatus.graphql
// → ../schema/types/LineItem.graphql
```

## Configuration

```csharp
b.GraphQL(g =>
{
    g.OutputDir = "../schema";
    g.FileLayout = GraphQLFileLayout.SingleFile;   // default; or FilePerType
    g.SingleFileName = "schema.graphql";           // default output file name
    g.EnumCasing = GraphQLEnumCasing.UpperSnake;   // default; PENDING, CONFIRMED, ...
    g.EmitDescriptions = true;                     // emit C# <summary> as GraphQL descriptions
    g.CustomScalars = true;                        // default; emit scalar DateTime / Date / Time / JSON declarations
});
```

When `CustomScalars` is enabled (the default), the emitter prepends scalar
declarations for non-standard types used in the schema:

```graphql
scalar DateTime
scalar Date
scalar JSON

type Order {
  ...
}
```

Set `CustomScalars = false` if your GraphQL server already defines these scalars
and you want to avoid duplicates.
