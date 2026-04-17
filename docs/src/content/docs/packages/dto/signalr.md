---
title: "Dto — Real-time with SignalR"
description: "[SignalRHub] generates a type-safe SignalR hub for any entity. Paired with [CrudApi], endpoints push OnCreated/OnUpdated/OnDeleted to connected clients automatically."
---

## Real-time push with `[SignalRHub]`

Add `[SignalRHub]` to an entity to generate a strongly-typed SignalR hub and client interface. When paired with `[CrudApi]`, the generated POST/PATCH/DELETE endpoints automatically inject `IHubContext` and push notifications to connected clients after each store operation.

### Standalone usage

```csharp
[SignalRHub]
public class Order
{
    public int Id { get; set; }
    public required string Product { get; set; }
    public decimal Total { get; set; }
}
```

This generates:

```csharp
// Generated hub + client interface
public interface IOrderHubClient
{
    Task OnCreated(OrderResponse item);
    Task OnUpdated(OrderResponse item);
    Task OnDeleted(int id);
}

public class OrderHub : Hub<IOrderHubClient> { }
```

### Paired with `[CrudApi]`

When both attributes are present, the generated CRUD endpoints inject `IHubContext<OrderHub, IOrderHubClient>` and push automatically:

```csharp
[CrudApi]
[SignalRHub]
public class Order
{
    [DtoIgnore(DtoTarget.Create | DtoTarget.Update | DtoTarget.Query)]
    public int Id { get; set; }
    public required string Product { get; set; }
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
}
```

The generated endpoints behave as:

| Endpoint | Store operation | SignalR push |
|---|---|---|
| `POST /api/orders` | Create | `Clients.All.OnCreated(response)` |
| `PATCH /api/orders/{id}` | Update | `Clients.All.OnUpdated(response)` |
| `DELETE /api/orders/{id}` | Delete | `Clients.All.OnDeleted(id)` |

### Setup

Map the generated hub in `Program.cs`:

```csharp
builder.Services.AddSignalR();

var app = builder.Build();

app.MapOrderEndpoints();                    // CRUD endpoints (generated)
app.MapHub<OrderHub>("/api/orders/hub");    // SignalR hub
```

### Client interface shape

The generated `IOrderHubClient` always follows the same pattern:

```csharp
public interface I{Entity}HubClient
{
    Task OnCreated({Entity}Response item);
    Task OnUpdated({Entity}Response item);
    Task OnDeleted(int id);            // key type matches the entity's primary key
}
```

### Frontend connection (TypeScript)

Connect from a JavaScript/TypeScript client using the `@microsoft/signalr` package:

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const connection = new HubConnectionBuilder()
  .withUrl("/api/orders/hub")
  .withAutomaticReconnect()
  .build();

connection.on("OnCreated", (order) => {
  console.log("New order:", order);
});

connection.on("OnUpdated", (order) => {
  console.log("Updated order:", order);
});

connection.on("OnDeleted", (id) => {
  console.log("Deleted order:", id);
});

await connection.start();
```

### Notes

- `[SignalRHub]` without `[CrudApi]` only generates the hub and interface — you call `IHubContext` yourself in custom endpoints or services.
- The hub class is `partial`, so you can add `OnConnectedAsync`/`OnDisconnectedAsync` overrides in a separate file.
- Group-based broadcasting (e.g. per-tenant) is not generated — use the partial hub to add group logic when needed.
