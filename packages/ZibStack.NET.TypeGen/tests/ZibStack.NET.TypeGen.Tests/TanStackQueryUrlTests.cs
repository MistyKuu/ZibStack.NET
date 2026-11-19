using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

public class TanStackQueryUrlTests
{
    // Execute the complete emitted TypeScript client, including a generated
    // endpoint, against a fetch stub. Node 22+ is installed by CI.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedClient_ResolvesActualRequestUrls(bool preserveBaseUrlPath)
    {
        var model = new SchemaModel();
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "get", Pattern = "/items/{id}", OperationId = "getItem",
            Parameters =
            {
                new EndpointParameter { Name = "id", Location = ParamLocation.Route, CSharpType = "string", Required = true },
            },
        });
        var settings = new GlobalSettings();
        settings.TanStackQuery.PreserveBaseUrlPath = preserveBaseUrlPath;
        settings.TanStackQuery.BaseUrlExpression = "configuredBase()";
        settings.TanStackQuery.EmitHooks = false;
        settings.TanStackQuery.EmitQueryOptions = false;
        settings.TanStackQuery.EmitMutationOptions = false;
        settings.TanStackQuery.EmitCacheHelpers = false;
        var client = Assert.Single(TanStackQueryEmitter.Emit(model, settings)).Content;
        var script = "const preserve = " + (preserveBaseUrlPath ? "true" : "false") + ";\n" + client + "\n" + """
            import assert from 'node:assert/strict';
            let base: string | undefined = undefined;
            let throwBase = false;
            function configuredBase() {
                if (throwBase) throw new Error('missing configuration');
                return base;
            }
            let actual = '';
            globalThis.fetch = async (url) => {
                actual = String(url);
                return new Response(null, { status: 204 });
            };
            let checks = 0;
            async function check(path: string, expected: string, query?: Record<string, unknown>) {
                await apiFetch(path, { query });
                assert.equal(actual, expected, `base=${base}, path=${path}, preserve=${preserve}`);
                checks++;
            }
            for (const prefix of ['', '/aoi', '/proxy/services/aoi']) {
                for (const trailing of ['', '/']) {
                    base = 'https://example.test' + prefix + trailing;
                    const service = 'https://example.test' + prefix + '/';
                    for (const route of ['asdf', '/asdf', '', '/']) {
                        const expected = preserve
                            ? service + route.replace(/^\/+/, '')
                            : new URL(route, base).toString();
                        await check(route, expected);
                    }
                    await check('https://other.test/remote?x=1', 'https://other.test/remote?x=1');
                    await check('//other.test/remote', 'https://other.test/remote');
                    const target = preserve ? service : 'https://example.test/';
                    await check('/asdf?keep=yes&replace=old', target + 'asdf?keep=yes&replace=new&tag=a&tag=b', {
                        replace: 'new', tag: ['a', 'b'], omitted: undefined, nil: null,
                    });
                    await getItem({ id: 'a/b ?#%ü' });
                    assert.equal(actual, target + 'items/a%2Fb%20%3F%23%25%C3%BC');
                    checks++;
                }
            }
            // Base queries/fragments retain URL semantics: route queries win;
            // an empty route inherits the base query but drops the fragment.
            base = 'https://example.test/aoi?base=1#fragment';
            await check('/asdf?route=2', preserve ? 'https://example.test/aoi/asdf?route=2' : 'https://example.test/asdf?route=2');
            await check('', preserve ? 'https://example.test/aoi/?base=1' : 'https://example.test/aoi?base=1');
            for (const fallback of [undefined, '']) {
                base = fallback;
                await check('/asdf', 'http://localhost/asdf');
                (globalThis as any).window = { location: { origin: 'https://browser.test' } };
                await check('/asdf', 'https://browser.test/asdf');
                delete (globalThis as any).window;
            }
            throwBase = true;
            await check('/asdf', 'http://localhost/asdf');
            (globalThis as any).window = { location: { origin: 'https://browser.test' } };
            await check('/asdf', 'https://browser.test/asdf');
            delete (globalThis as any).window;
            console.log(`Verified ${checks} request URLs`);
            """;
        var dir = Path.Combine(Path.GetTempPath(), "typegen-url-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "client.mts");
            await File.WriteAllTextAsync(path, script);
            var start = new ProcessStartInfo("node")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            start.ArgumentList.Add("--experimental-transform-types");
            start.ArgumentList.Add(path);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("Generated-client URL test timed out.");
            }
            Assert.True(process.ExitCode == 0, $"{await stdout}\n{await stderr}\n{client}");
            Assert.Contains("Verified 56 request URLs", await stdout);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
