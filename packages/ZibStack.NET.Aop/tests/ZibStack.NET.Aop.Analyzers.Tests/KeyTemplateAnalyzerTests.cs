using Xunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.CSharp.Testing;
using ZibStack.NET.Aop.Generator;

namespace ZibStack.NET.Aop.Analyzers.Tests;

using Verify = CSharpAnalyzerVerifier<KeyTemplateAnalyzer, DefaultVerifier>;

public class KeyTemplateAnalyzerTests
{
    private const string CacheAttributeStub = @"
namespace ZibStack.NET.Aop
{
    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public class AspectAttribute : System.Attribute
    {
        public int Order { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class CacheAttribute : AspectAttribute
    {
        public int DurationSeconds { get; set; } = 300;
        public string? KeyTemplate { get; set; }
    }

    [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
    public sealed class HybridCacheAttribute : AspectAttribute
    {
        public int DurationSeconds { get; set; } = 300;
        public string? KeyTemplate { get; set; }
    }
}
";

    [Fact]
    public async Task ValidPlaceholder_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Cache(KeyTemplate = ""product:{id}"")]
    public string Get(int id) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task ValidNestedPlaceholder_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Req { public int CustomerId { get; set; } }

public class Svc
{
    [Cache(KeyTemplate = ""order:{req.CustomerId}"")]
    public string Get(Req req) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task MultiplePlaceholders_AllValid_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Cache(KeyTemplate = ""{a}:{b}"")]
    public string Get(int a, int b) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InvalidPlaceholder_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:Cache(KeyTemplate = ""product:{typo}"")|}]
    public string Get(int id) => """";
}
" + CacheAttributeStub;

        var expected = Verify.Diagnostic(KeyTemplateAnalyzer.InvalidPlaceholderId)
            .WithLocation(0)
            .WithArguments("typo", "typo", "Get", "id");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvalidNestedRoot_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Req { public int Id { get; set; } }

public class Svc
{
    [{|#0:Cache(KeyTemplate = ""order:{request.Id}"")|}]
    public string Get(Req req) => """";
}
" + CacheAttributeStub;

        var expected = Verify.Diagnostic(KeyTemplateAnalyzer.InvalidPlaceholderId)
            .WithLocation(0)
            .WithArguments("request.Id", "request", "Get", "req");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NoKeyTemplate_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Cache(DurationSeconds = 60)]
    public string Get(int id) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task HybridCache_InvalidPlaceholder_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [{|#0:HybridCache(KeyTemplate = ""item:{wrong}"")|}]
    public string Get(int id) => """";
}
" + CacheAttributeStub;

        var expected = Verify.Diagnostic(KeyTemplateAnalyzer.InvalidPlaceholderId)
            .WithLocation(0)
            .WithArguments("wrong", "wrong", "Get", "id");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task NoPlaceholders_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Svc
{
    [Cache(KeyTemplate = ""static-key"")]
    public string Get(int id) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    // ── ZBAOP002: nested property path validation ────────────────────────

    [Fact]
    public async Task ValidNestedPath_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Customer { public int Id { get; set; } }
public class Order { public Customer Customer { get; set; } }

public class Svc
{
    [Cache(KeyTemplate = ""order:{order.Customer.Id}"")]
    public string Get(Order order) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }

    [Fact]
    public async Task InvalidNestedProperty_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Req { public int CustomerId { get; set; } }

public class Svc
{
    [{|#0:Cache(KeyTemplate = ""key:{req.Typo}"")|}]
    public string Get(Req req) => """";
}
" + CacheAttributeStub;

        var expected = Verify.Diagnostic(KeyTemplateAnalyzer.InvalidPropertyPathId)
            .WithLocation(0)
            .WithArguments("req.Typo", "req", "Typo");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InvalidDeepNestedProperty_Reports()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Inner { public int Id { get; set; } }
public class Outer { public Inner Inner { get; set; } }

public class Svc
{
    [{|#0:Cache(KeyTemplate = ""key:{o.Inner.Nope}"")|}]
    public string Get(Outer o) => """";
}
" + CacheAttributeStub;

        var expected = Verify.Diagnostic(KeyTemplateAnalyzer.InvalidPropertyPathId)
            .WithLocation(0)
            .WithArguments("o.Inner.Nope", "o.Inner", "Nope");

        await Verify.VerifyAnalyzerAsync(test, expected);
    }

    [Fact]
    public async Task InheritedProperty_NoDiagnostic()
    {
        var test = @"
using ZibStack.NET.Aop;

public class Base { public int Id { get; set; } }
public class Derived : Base { public string Name { get; set; } }

public class Svc
{
    [Cache(KeyTemplate = ""key:{item.Id}:{item.Name}"")]
    public string Get(Derived item) => """";
}
" + CacheAttributeStub;

        await Verify.VerifyAnalyzerAsync(test);
    }
}
