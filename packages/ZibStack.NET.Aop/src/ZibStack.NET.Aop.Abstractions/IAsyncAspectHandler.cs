using System.Threading.Tasks;

namespace ZibStack.NET.Aop;

/// <summary>
/// Async runtime handler for custom aspects. All hooks return <see cref="ValueTask"/>
/// for zero-allocation on sync completion paths.
/// Can only be used on async methods. On sync methods, use <see cref="IAspectHandler"/> instead.
/// </summary>
public interface IAsyncAspectHandler
{
    ValueTask OnBeforeAsync(AspectContext context);
    ValueTask OnAfterAsync(AspectContext context);
    ValueTask OnExceptionAsync(AspectContext context, Exception exception);
}
