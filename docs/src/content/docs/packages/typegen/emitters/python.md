---
title: TypeGen — Python emitter (Pydantic v2)
description: "TypeTarget.Python emits Pydantic v2 BaseModel subclasses (or plain dataclasses) from C# DTOs — idiomatic for FastAPI backends consuming the same contract."
---

`TypeTarget.Python` emits Pydantic v2 `BaseModel` subclasses — idiomatic for
FastAPI backends consuming the same contract as the C# DTOs. Class names stay
PascalCase; property names get snake_cased (PEP 8) with `Field(alias="…")`
preserving the C# casing on JSON parse / serialize.

```csharp
[GenerateTypes(Targets = TypeTarget.Python, OutputDir = "../api_models")]
public class Order
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public OrderStatus Status { get; set; }
    public string? Note { get; set; }
}
```

```python
# order.py
from __future__ import annotations
from pydantic import BaseModel, Field
from order_status import OrderStatus

class Order(BaseModel):
    id: int = Field(alias="Id")
    customer: str = Field(alias="Customer")
    status: OrderStatus = Field(alias="Status")
    note: str | None = Field(default=None, alias="Note")
```

Switch to plain stdlib dataclasses (no Pydantic dependency) via the configurator:
`b.Python(py => py.Style = PythonStyle.Dataclass);`. Single-file bundle vs
file-per-class via `py.FileLayout`. Disable snake_case conversion with
`py.SnakeCaseProperties = false`.
