using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZibStack.NET.Aop.Generator;
using Xunit;

namespace ZibStack.NET.Aop.Tests;

public class AopParserTests
{
    [Fact]
    public void ParseMethod_ReturnsNull_WhenNoAspectAttribute()
    {
        var (method, _) = GetMethodSymbol(@"
public class Svc { public void DoWork() { } }
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.Null(result);
    }

    [Fact]
    public void ParseMethod_ReturnsModel_WhenHasAspect()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public int Add(int a, int b) => a + b;
}
", "Add");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Equal("Add", result!.MethodName);
        Assert.Equal(2, result.Parameters.Count);
        Assert.Single(result.Aspects);
        Assert.Contains("MyAspectAttribute", result.Aspects[0].AttributeFullName);
    }

    [Fact]
    public void ParseMethod_ReturnsNull_ForStaticMethod()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public static void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.Null(result);
    }

    [Fact]
    public void ParseMethod_DetectsAsyncMethod()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public async Task<int> AddAsync(int a, int b) { await Task.CompletedTask; return a + b; }
}
", "AddAsync");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.IsAsync);
        Assert.False(result.ReturnsVoid);
    }

    [Fact]
    public void ParseMethod_DetectsVoidAsyncMethod()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using System.Threading.Tasks;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public async Task DoWorkAsync() { await Task.CompletedTask; }
}
", "DoWorkAsync");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.IsAsync);
        Assert.True(result.ReturnsVoid); // Task without <T> = void for logging
    }

    [Fact]
    public void ParseMethod_DetectsMultipleAspects()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H1))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class A1Attribute : AspectAttribute { }

[AspectHandler(typeof(H2))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class A2Attribute : AspectAttribute { }

public class H1 : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}
public class H2 : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [A1] [A2]
    public void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Aspects.Count);
    }

    [Fact]
    public void ParseMethod_DetectsAroundHandler()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using System;

[AspectHandler(typeof(AH))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class AroundAttribute : AspectAttribute { }

public class AH : IAroundAspectHandler
{
    public object? Around(AspectContext c, Func<object?> p) => p();
}

public class Svc
{
    [Around]
    public int Add(int a, int b) => a + b;
}
", "Add");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.Aspects[0].IsAroundHandler);
    }

    [Fact]
    public void ParseMethod_DetectsSensitiveParam()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using ZibStack.NET.Log;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public void Login(string user, [Sensitive] string pass) { }
}
", "Login");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.False(result!.Parameters[0].IsSensitive);
        Assert.True(result.Parameters[1].IsSensitive);
    }

    [Fact]
    public void ParseMethod_DetectsNoLogParam()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using ZibStack.NET.Log;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [MyAspect]
    public void Upload(string name, [NoLog] byte[] data) { }
}
", "Upload");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.False(result!.Parameters[0].IsNoLog);
        Assert.True(result.Parameters[1].IsNoLog);
    }

    [Fact]
    public void ParseMethod_PicksUpClassLevelAspect()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

[MyAspect]
public class Svc
{
    public void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Single(result!.Aspects);
    }

    [Fact]
    public void ParseMethod_MethodLevelOverridesClassLevel()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

[MyAspect]
public class Svc
{
    [MyAspect]  // same aspect on method — should NOT duplicate
    public void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Single(result!.Aspects); // not 2
    }

    [Fact]
    public void ParseMethod_RespectsAspectOrder()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
public class A1Attribute : AspectAttribute { }

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true)]
public class A2Attribute : AspectAttribute { }

public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public class Svc
{
    [A2(Order = 10)]
    [A1(Order = 1)]
    public void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Aspects.Count);
        Assert.Contains("A1", result.Aspects[0].AttributeFullName); // Order=1 first
        Assert.Contains("A2", result.Aspects[1].AttributeFullName); // Order=10 second
    }

    // === Helper ===

    private static (IMethodSymbol method, Compilation compilation) GetMethodSymbol(string source, string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZibStack.NET.Aop.AspectAttribute).Assembly.Location),
        };

        try { references.Add(MetadataReference.CreateFromFile(typeof(ZibStack.NET.Log.SensitiveAttribute).Assembly.Location)); } catch { }

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        foreach (var dll in new[] { "System.Runtime.dll", "netstandard.dll", "System.Threading.Tasks.dll" })
        {
            var path = Path.Combine(runtimeDir, dll);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        var compilation = CSharpCompilation.Create("TestAsm",
            new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var model = compilation.GetSemanticModel(syntaxTree);
        var methodSyntax = syntaxTree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First(m => m.Identifier.Text == methodName);

        var methodSymbol = model.GetDeclaredSymbol(methodSyntax)!;
        return (methodSymbol, compilation);
    }
}
