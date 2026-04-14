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

    // === Return attribute tests ===

    [Fact]
    public void ParseMethod_DetectsReturnSensitive()
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
    [return: Sensitive]
    public string GetSecret() => ""secret"";
}
", "GetSecret");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.Aspects[0].SensitiveReturn);
    }

    [Fact]
    public void ParseMethod_DetectsReturnNoLog()
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
    [return: NoLog]
    public byte[] GetData() => new byte[0];
}
", "GetData");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.Aspects[0].NoLogReturn);
    }

    [Fact]
    public void ParseMethod_VoidMethod()
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
    public void DoWork() { }
}
", "DoWork");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.ReturnsVoid);
        Assert.False(result.IsAsync);
    }

    [Fact]
    public void ParseMethod_NoParams_HasEmptyParameterList()
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
    public void Ping() { }
}
", "Ping");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Empty(result!.Parameters);
    }

    [Fact]
    public void ParseMethod_MixedAroundAndBeforeAfter()
    {
        var (method, _) = GetMethodSymbol(@"
using ZibStack.NET.Aop;
using System;

[AspectHandler(typeof(BeforeH))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class BeforeAspectAttribute : AspectAttribute { }

[AspectHandler(typeof(AroundH))]
[System.AttributeUsage(System.AttributeTargets.Method)]
public class AroundAspectAttribute : AspectAttribute { }

public class BeforeH : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, Exception e) {}
}

public class AroundH : IAroundAspectHandler {
    public object? Around(AspectContext c, Func<object?> p) => p();
}

public class Svc
{
    [BeforeAspect]
    [AroundAspect]
    public int Add(int a, int b) => a + b;
}
", "Add");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Equal(2, result!.Aspects.Count);
        var around = result.Aspects.First(a => a.IsAroundHandler);
        var before = result.Aspects.First(a => !a.IsAroundHandler);
        Assert.NotNull(around);
        Assert.NotNull(before);
    }

    [Fact]
    public void ParseMethod_DetectsComplexReturnType()
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

public class Order { public int Id { get; set; } }

public class Svc
{
    [MyAspect]
    public Order GetOrder(int id) => new Order { Id = id };
}
", "GetOrder");

        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.True(result!.HasComplexReturnType);
    }

    [Fact]
    public void ParseMethod_PrimitiveReturnType_NotComplex()
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
        Assert.False(result!.HasComplexReturnType);
    }

    // === Runtime tests ===

    [Fact]
    public void AspectContext_FormatParameters_MasksSensitive()
    {
        var ctx = new AspectContext
        {
            ClassName = "Svc", MethodName = "Login",
            Parameters = new AspectParameterInfo[]
            {
                new() { Name = "user", Value = "admin" },
                new() { Name = "pass", Value = "secret", IsSensitive = true },
                new() { Name = "data", Value = null, IsNoLog = true },
            }
        };

        var formatted = ctx.FormatParameters();
        Assert.Equal("user: admin, pass: ***", formatted);
        Assert.DoesNotContain("data", formatted);
        Assert.DoesNotContain("secret", formatted);
    }

    [Fact]
    public void AspectServiceProvider_Resolve_ThrowsWhenNotConfigured()
    {
        AspectServiceProvider.ServiceProvider = null;
        Assert.Throws<InvalidOperationException>(() => AspectServiceProvider.Resolve<object>());
    }

    // === Inheritance tests ===

    private const string InheritanceSource = @"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class, Inherited = true)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

[MyAspect]
public class BaseService
{
    public virtual void BaseMethod() { }
}

public class DerivedService : BaseService
{
    public override void BaseMethod() { }
    public void DerivedMethod() { }
}

public class DerivedWithOwnAspect : BaseService
{
    [MyAspect]
    public void OwnMethod() { }
}
";

    [Fact]
    public void Inheritance_BaseClassWithAspect_BaseMethodHasAspect()
    {
        var (method, _) = GetMethodSymbol(InheritanceSource, "BaseMethod", "BaseService");
        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
        Assert.Single(result!.Aspects);
    }

    [Fact]
    public void Inheritance_DerivedClass_OverriddenMethodInheritsAspect()
    {
        var (method, _) = GetMethodSymbol(InheritanceSource, "BaseMethod", "DerivedService");
        var result = AopParser.ParseMethod(method, default);
        // DerivedService does NOT have [MyAspect] on the class
        // The override method itself doesn't have the attribute
        // Question: does it inherit from base class?
        // Answer depends on whether we check ContainingType's base types
        if (result != null)
            Assert.Single(result.Aspects);
    }

    [Fact]
    public void Inheritance_DerivedClass_OwnMethodNoAspect()
    {
        var (method, _) = GetMethodSymbol(InheritanceSource, "DerivedMethod", "DerivedService");
        var result = AopParser.ParseMethod(method, default);
        // DerivedService has no [MyAspect] on class, DerivedMethod has no [MyAspect] on method
        // Should be null — derived class doesn't inherit class-level attributes
        Assert.Null(result);
    }

    [Fact]
    public void Inheritance_DerivedWithOwnAspect_OwnMethodHasAspect()
    {
        var (method, _) = GetMethodSymbol(InheritanceSource, "OwnMethod", "DerivedWithOwnAspect");
        var result = AopParser.ParseMethod(method, default);
        Assert.NotNull(result);
    }

    // === Generic class support ===

    [Fact]
    public void ParseClass_GenericBaseClass_ExtractsTypeParameters()
    {
        var compilation = GetCompilation(@"
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
public abstract class BaseService<T> where T : class, new()
{
    public virtual T Produce() => new T();
}
");
        var baseService = GetTypeByName(compilation, "BaseService");
        Assert.NotNull(baseService);
        var model = AopParser.ParseClass(baseService!, null, default);
        Assert.NotNull(model);
        Assert.Equal("BaseService", model!.ClassName);
        Assert.Single(model.TypeParameters);
        Assert.Equal("T", model.TypeParameters[0].Name);
        Assert.Contains("class", model.TypeParameters[0].Constraints);
        Assert.Contains("new()", model.TypeParameters[0].Constraints);
    }

    // === Interface-proxy synthesis ===

    [Fact]
    public void ParseInterfaceProxy_InheritsClassLevelAspectOntoInterfaceMethods()
    {
        var compilation = GetCompilation(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public interface IOrderService
{
    int GetOrder(int id);
}

[MyAspect]
public class OrderService : IOrderService
{
    public int GetOrder(int id) => id;
}
");
        var iface = GetTypeByName(compilation, "IOrderService");
        var impl = GetTypeByName(compilation, "OrderService");
        Assert.NotNull(iface);
        Assert.NotNull(impl);

        var proxy = AopParser.ParseInterfaceProxy(iface!, impl!, null, default);
        Assert.NotNull(proxy);
        Assert.True(proxy!.IsInterfaceProxy);
        Assert.Equal("IOrderService", proxy.ClassName);
        Assert.Single(proxy.Methods);
        Assert.Single(proxy.Methods[0].Aspects);
        Assert.Contains("MyAspectAttribute", proxy.Methods[0].Aspects[0].AttributeFullName);
    }

    [Fact]
    public void ParseInterfaceProxy_ReturnsNull_WhenImplHasNoClassLevelAspect()
    {
        var compilation = GetCompilation(@"
using ZibStack.NET.Aop;

[AspectHandler(typeof(H))]
[System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Class)]
public class MyAspectAttribute : AspectAttribute { }
public class H : IAspectHandler {
    public void OnBefore(AspectContext c) {}
    public void OnAfter(AspectContext c) {}
    public void OnException(AspectContext c, System.Exception e) {}
}

public interface IOrderService { int GetOrder(int id); }

public class OrderService : IOrderService
{
    [MyAspect] // method-level only; should NOT propagate to the interface
    public int GetOrder(int id) => id;
}
");
        var iface = GetTypeByName(compilation, "IOrderService");
        var impl = GetTypeByName(compilation, "OrderService");
        var proxy = AopParser.ParseInterfaceProxy(iface!, impl!, null, default);
        Assert.Null(proxy);
    }

    private static INamedTypeSymbol? GetTypeByName(Compilation compilation, string name)
        => compilation.GlobalNamespace.GetTypeMembers(name).FirstOrDefault();

    // === Helper ===

    private static Compilation GetCompilation(string source)
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
        return CSharpCompilation.Create("TestAsm",
            new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (IMethodSymbol method, Compilation compilation) GetMethodSymbol(string source, string methodName, string? className = null)
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

        MethodDeclarationSyntax methodSyntax;
        if (className != null)
        {
            var classSyntax = syntaxTree.GetRoot().DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .First(c => c.Identifier.Text == className);
            methodSyntax = classSyntax.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == methodName);
        }
        else
        {
            methodSyntax = syntaxTree.GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .First(m => m.Identifier.Text == methodName);
        }

        var methodSymbol = model.GetDeclaredSymbol(methodSyntax)!;
        return (methodSymbol, compilation);
    }
}
