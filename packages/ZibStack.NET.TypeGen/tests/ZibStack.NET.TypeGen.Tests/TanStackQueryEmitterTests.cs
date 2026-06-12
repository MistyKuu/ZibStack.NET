using System.Linq;
using Xunit;
using ZibStack.NET.TypeGen.Generator;

namespace TypeGenTests;

public class TanStackQueryEmitterTests
{
    private static SchemaClass Cls(
        string name,
        TypeTarget targets = TypeTarget.TypeScript | TypeTarget.TanStackQuery,
        params (string Name, string CSharpType, bool Nullable)[] props)
    {
        var cls = new SchemaClass
        {
            CSharpFullName = name,
            SourceName = name,
            EmittedName = name,
            OutputDir = "models",
            Targets = targets,
        };
        foreach (var (propName, type, nullable) in props)
            cls.Properties.Add(new SchemaProperty { SourceName = propName, CSharpTypeFullName = type, IsNullable = nullable });
        return cls;
    }

    private static SchemaModel ModelWith(params SchemaClass[] classes)
    {
        var model = new SchemaModel();
        model.Classes.AddRange(classes);
        return model;
    }

    [Fact]
    public void ComplexEndpoint_EmitsTypedQueryAndMutationSurface()
    {
        var model = ModelWith(
            Cls("JobSummary", props: new[] { ("Id", "System.Guid", false), ("Name", "string", false) }),
            Cls("StartJobRequest", props: new[] { ("TemplateId", "System.Guid", false), ("Priority", "int", true) }));
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "get",
            Pattern = "/api/workspaces/{workspaceId:guid}/reports/{reportId:int}/jobs",
            OperationId = "getWorkspaceReportJobs",
            Tag = "Reports",
            IsListEndpoint = true,
            ResponseCSharpType = "PaginatedResponse<JobSummary>",
            Parameters =
            {
                new EndpointParameter { Name = "workspaceId", Location = ParamLocation.Route, CSharpType = "System.Guid", Required = true },
                new EndpointParameter { Name = "reportId", Location = ParamLocation.Route, CSharpType = "int", Required = true },
                new EndpointParameter { Name = "includeHistory", Location = ParamLocation.Query, CSharpType = "bool", Required = false },
                new EndpointParameter { Name = "minimumBudget", Location = ParamLocation.Query, CSharpType = "decimal", Required = false },
            },
        });
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "post",
            Pattern = "/api/workspaces/{workspaceId:guid}/jobs",
            OperationId = "startWorkspaceJob",
            Tag = "Jobs",
            RequestBodyCSharpType = "StartJobRequest",
            ResponseCSharpType = "JobSummary",
            Parameters =
            {
                new EndpointParameter { Name = "workspaceId", Location = ParamLocation.Route, CSharpType = "System.Guid", Required = true },
            },
        });

        var settings = new GlobalSettings { HasQueryDsl = true };
        settings.TanStackQuery.OutputDir = "client/api";
        settings.TanStackQuery.ModelsImportPath = "../models";
        settings.TanStackQuery.BaseUrlExpression = "window.location.origin";

        var file = Assert.Single(TanStackQueryEmitter.Emit(model, settings));
        var ts = file.Content;

        Assert.Equal(TypeTarget.TanStackQuery, file.Target);
        Assert.Equal("api.gen.ts", file.FileName);
        Assert.Contains("from '@tanstack/react-query';", ts);
        Assert.Contains("import type { JobSummary, StartJobRequest } from '../models';", ts);
        Assert.Contains("try { return window.location.origin; } catch { return undefined; }", ts);
        Assert.Contains("const baseUrl = configuredBaseUrl || (typeof window !== 'undefined' ? window.location.origin : 'http://localhost');", ts);
        Assert.Contains("const url = new URL(path, baseUrl);", ts);

        Assert.Contains("export type PaginatedResponseOfJobSummary = {", ts);
        Assert.Contains("items: JobSummary[];", ts);
        Assert.Contains("export const reportsKeys = {", ts);
        Assert.Contains("getWorkspaceReportJobs: (input: GetWorkspaceReportJobsInput)", ts);

        Assert.Contains("export type GetWorkspaceReportJobsInput = {", ts);
        Assert.Contains("workspaceId: string;", ts);
        Assert.Contains("reportId: number;", ts);
        Assert.Contains("includeHistory?: boolean;", ts);
        Assert.Contains("minimumBudget?: string;", ts);
        Assert.Contains("page?: number;", ts);
        Assert.Contains("pageSize?: number;", ts);
        Assert.Contains("filter?: string;", ts);
        Assert.Contains("count?: boolean;", ts);

        Assert.Contains("/api/workspaces/${encodeURIComponent(String(input.workspaceId))}/reports/${encodeURIComponent(String(input.reportId))}/jobs", ts);
        Assert.Contains("includeHistory: input.includeHistory", ts);
        Assert.Contains("minimumBudget: input.minimumBudget", ts);
        Assert.Contains("pageSize: input.pageSize", ts);
        Assert.Contains("export function getWorkspaceReportJobsOptions(input: GetWorkspaceReportJobsInput)", ts);
        Assert.Contains("return useQuery(getWorkspaceReportJobsOptions(input));", ts);
        Assert.Contains("export function prefetchGetWorkspaceReportJobs(queryClient: QueryClient, input: GetWorkspaceReportJobsInput)", ts);
        Assert.Contains("export function invalidateReportsQueries(queryClient: QueryClient)", ts);

        Assert.Contains("export type StartWorkspaceJobInput = {", ts);
        Assert.Contains("export const jobsKeys = {", ts);
        Assert.Contains("body: StartJobRequest;", ts);
        Assert.Contains("body: input.body", ts);
        Assert.Contains("export function startWorkspaceJobMutationOptions()", ts);
        Assert.Contains("return useMutation({", ts);
        Assert.Contains("await invalidateJobsQueries(queryClient);", ts);
    }

    [Fact]
    public void SplitByTag_WithCustomClient_EmitsOneFilePerTagWithoutDefaultClient()
    {
        var model = ModelWith(Cls("JobSummary"));
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "get",
            Pattern = "/reports",
            OperationId = "listReports",
            Tag = "Reports",
            ResponseArrayItemCSharpType = "JobSummary",
        });
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "post",
            Pattern = "/jobs/retry",
            OperationId = "retryJob",
            Tag = "Jobs",
            ResponseCSharpType = "JobSummary",
        });

        var settings = new GlobalSettings();
        settings.TanStackQuery.FileLayout = QueryFileLayout.SplitByTag;
        settings.TanStackQuery.ApiClientImportPath = "./http-client";
        settings.TanStackQuery.ApiClientName = "request";
        settings.TanStackQuery.EmitHooks = false;
        settings.TanStackQuery.EmitCacheHelpers = false;

        var files = TanStackQueryEmitter.Emit(model, settings);

        Assert.Equal(2, files.Count);
        var reports = files.Single(f => f.FileName == "reports.gen.ts").Content;
        var jobs = files.Single(f => f.FileName == "jobs.gen.ts").Content;

        Assert.Contains("import { request } from './http-client';", reports);
        Assert.DoesNotContain("export type ApiFetchOptions", reports);
        Assert.Contains("export const reportsKeys", reports);
        Assert.DoesNotContain("useQuery", reports);
        Assert.Contains("import { request } from './http-client';", jobs);
        Assert.Contains("mutationOptions", jobs);
        Assert.DoesNotContain("useMutation", jobs);
    }

    [Fact]
    public void CrudApiClass_TargetingTanStackQuery_SynthesizesEndpointClient()
    {
        var order = Cls("Order", TypeTarget.TanStackQuery | TypeTarget.TypeScript,
            ("Id", "int", false),
            ("Name", "string", false));
        order.Crud = new CrudApiInfo { Operations = CrudOperations.GetList | CrudOperations.Create };

        var ts = TanStackQueryEmitter.Emit(ModelWith(order), new GlobalSettings()).Single().Content;

        Assert.Contains("export const orderKeys = {", ts);
        Assert.Contains("export function listOrder(input: ListOrderInput = {}, signal?: AbortSignal)", ts);
        Assert.Contains("export type PaginatedResponseOfOrder", ts);
        Assert.Contains("export function createOrder(input: CreateOrderInput", ts);
        Assert.Contains("body: CreateOrderRequest;", ts);
    }

    [Fact]
    public void UsesTypeScriptEmitterNames_ForModelImportsAndResponseTypes()
    {
        var model = ModelWith(Cls("OrderDto"));
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "get",
            Pattern = "/orders/{id:int}",
            OperationId = "getOrder",
            Tag = "Orders",
            ResponseCSharpType = "OrderDto",
            Parameters =
            {
                new EndpointParameter { Name = "id", Location = ParamLocation.Route, CSharpType = "int", Required = true },
            },
        });

        var settings = new GlobalSettings();
        settings.TypeScript.StripSuffixes.Add("Dto");

        _ = TypeScriptEmitter.Emit(model, settings);
        var ts = TanStackQueryEmitter.Emit(model, settings).Single().Content;

        Assert.Contains("import type { Order } from './Order';", ts);
        Assert.Contains("Promise<Order>", ts);
        Assert.DoesNotContain("OrderDto", ts);
    }

    [Fact]
    public void MissingRouteParameters_AreAddedAsRequiredInputMembers()
    {
        var model = ModelWith(Cls("JobSummary"));
        model.Endpoints.Add(new EndpointInfo
        {
            Verb = "get",
            Pattern = "/tenants/{tenantId:guid}/jobs/{jobId:int}/budgets/{minimumBudget:decimal}",
            OperationId = "getJobBudget",
            Tag = "Jobs",
            ResponseCSharpType = "JobSummary",
        });

        var ts = TanStackQueryEmitter.Emit(model, new GlobalSettings()).Single().Content;

        Assert.Contains("tenantId: string;", ts);
        Assert.Contains("jobId: number;", ts);
        Assert.Contains("minimumBudget: string;", ts);
        Assert.Contains("${encodeURIComponent(String(input.tenantId))}", ts);
        Assert.Contains("${encodeURIComponent(String(input.jobId))}", ts);
        Assert.Contains("${encodeURIComponent(String(input.minimumBudget))}", ts);
        Assert.DoesNotContain("String(undefined)", ts);
    }
}
