using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using ZibStack.NET.Validation;

namespace ZibStack.NET.Dto.Sample.Models;

// Zero Dto attributes on the class — Create/Update DTOs come from the fluent
// IDtoConfigurator (DtoConfig.cs). Per-property validation attributes still
// apply (locality wins for those — see DtoConfig.cs notes).
public partial class Article
{
    public int Id { get; set; }

    [ZRequired] [ZMaxLength(200)]
    public required string Title { get; set; }

    [ZMaxLength(10_000)]
    public string? Body { get; set; }

    public DateTime PublishedAt { get; set; }

    // Catches unmapped JSON keys at deserialization time. TypeGen detects this
    // and bumps the schema with `additionalProperties: true` (OpenAPI) /
    // `[key: string]: unknown;` index signature (TypeScript). The Extra property
    // itself isn't emitted as a regular field.
    [JsonExtensionData]
    public Dictionary<string, object?>? Extra { get; set; }
}
