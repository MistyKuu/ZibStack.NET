using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZibStack.NET.Validation;

public sealed partial class ValidationGenerator
{
    private static ValidationInfo? ExtractValidationInfo(GeneratorAttributeSyntaxContext context)
    {
        try
        {
            var symbol = (INamedTypeSymbol)context.TargetSymbol;
            var ns = symbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : symbol.ContainingNamespace.ToDisplayString();

            var isRecord = context.TargetNode is RecordDeclarationSyntax;
            var isStruct = context.TargetNode is StructDeclarationSyntax;

            var properties = new List<PropertyValidationInfo>();
            var nestedProps = new List<NestedValidatableInfo>();

            foreach (var member in symbol.GetMembers())
            {
                if (member is not IPropertySymbol prop) continue;
                if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.GetMethod is null) continue;

                var rules = new List<ValidationRule>();
                var isNullableRef = prop.Type.NullableAnnotation == NullableAnnotation.Annotated;
                var isValueType = prop.Type.IsValueType;
                var typeName = prop.Type.ToDisplayString();

                foreach (var attr in prop.GetAttributes())
                {
                    var attrName = attr.AttributeClass?.ToDisplayString();
                    if (attrName is null) continue;

                    var customMessage = attr.NamedArguments
                        .FirstOrDefault(a => a.Key == "Message").Value.Value as string;

                    switch (attrName)
                    {
                        case "ZibStack.NET.Validation.ZRequiredAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.Required, customMessage));
                            break;

                        case "ZibStack.NET.Validation.ZMinLengthAttribute":
                            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int minLen)
                                rules.Add(new ValidationRule(ValidationRuleKind.MinLength, customMessage, minValue: minLen));
                            break;

                        case "ZibStack.NET.Validation.ZMaxLengthAttribute":
                            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is int maxLen)
                                rules.Add(new ValidationRule(ValidationRuleKind.MaxLength, customMessage, maxValue: maxLen));
                            break;

                        case "ZibStack.NET.Validation.ZRangeAttribute":
                            if (attr.ConstructorArguments.Length >= 2)
                            {
                                var min = attr.ConstructorArguments[0].Value is double dmin ? dmin : 0;
                                var max = attr.ConstructorArguments[1].Value is double dmax ? dmax : 0;
                                rules.Add(new ValidationRule(ValidationRuleKind.Range, customMessage, minValue: min, maxValue: max));
                            }
                            break;

                        case "ZibStack.NET.Validation.ZEmailAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.Email, customMessage));
                            break;

                        case "ZibStack.NET.Validation.ZUrlAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.Url, customMessage));
                            break;

                        case "ZibStack.NET.Validation.ZMatchAttribute":
                            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string pattern)
                                rules.Add(new ValidationRule(ValidationRuleKind.Match, customMessage, pattern: pattern));
                            break;

                        case "ZibStack.NET.Validation.ZNotEmptyAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.NotEmpty, customMessage));
                            break;

                        case "ZibStack.NET.Validation.ZInAttribute":
                            if (attr.ConstructorArguments.Length > 0)
                            {
                                var values = attr.ConstructorArguments[0].Values
                                    .Select(v => v.Value as string)
                                    .Where(v => v != null)
                                    .Select(v => v!)
                                    .ToArray();
                                rules.Add(new ValidationRule(ValidationRuleKind.In, customMessage, allowedValues: values));
                            }
                            break;

                        case "ZibStack.NET.Validation.ZNotInAttribute":
                            if (attr.ConstructorArguments.Length > 0)
                            {
                                var values = attr.ConstructorArguments[0].Values
                                    .Select(v => v.Value as string)
                                    .Where(v => v != null)
                                    .Select(v => v!)
                                    .ToArray();
                                rules.Add(new ValidationRule(ValidationRuleKind.NotIn, customMessage, allowedValues: values));
                            }
                            break;

                        case "ZibStack.NET.Validation.ZCreditCardAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.CreditCard, customMessage));
                            break;

                        case "ZibStack.NET.Validation.ZPhoneAttribute":
                            rules.Add(new ValidationRule(ValidationRuleKind.Phone, customMessage));
                            break;
                    }
                }

                // Check for [ZCascade] attribute
                var hasCascade = prop.GetAttributes()
                    .Any(a => a.AttributeClass?.ToDisplayString() == "ZibStack.NET.Validation.ZCascadeAttribute");

                if (rules.Count > 0)
                {
                    var propInfo = new PropertyValidationInfo(
                        prop.Name, typeName, isNullableRef, isValueType, rules);
                    propInfo.CascadeStopOnFirst = hasCascade;
                    properties.Add(propInfo);
                }

                // Check if property type is IValidatable (has [ZValidate] or Configure method)
                var propType = prop.Type;
                if (propType.NullableAnnotation == NullableAnnotation.Annotated && propType is INamedTypeSymbol nts && nts.TypeArguments.Length == 1)
                    propType = nts.TypeArguments[0]; // unwrap Nullable<T>

                // Check for collection: IEnumerable<T> where T is validatable
                var elementType = GetCollectionElementType(propType);
                var typeToCheck = elementType ?? propType;

                if (typeToCheck is INamedTypeSymbol namedCheck)
                {
                    bool isValidatable = namedCheck.GetAttributes()
                        .Any(a => a.AttributeClass?.ToDisplayString() == ZValidateAttributeFqn);
                    if (!isValidatable)
                        isValidatable = namedCheck.GetMembers().OfType<IMethodSymbol>()
                            .Any(m => m.Name == "Configure" && m.Parameters.Length == 1
                                && m.Parameters[0].Type.Name == "IValidationBuilder");
                    if (isValidatable)
                    {
                        nestedProps.Add(new NestedValidatableInfo
                        {
                            PropertyName = prop.Name,
                            IsNullable = isNullableRef || prop.Type.NullableAnnotation == NullableAnnotation.Annotated,
                            IsCollection = elementType != null,
                        });
                    }
                }
            }

            // Parse cross-field rules from Configure(IValidationBuilder<T>) method.
            var crossFieldRules = new List<CrossFieldRule>();
            var conditionalRules = new List<ConditionalRule>();
            var ruleSetRules = new List<RuleSetRule>();
            if (context.TargetNode is TypeDeclarationSyntax tds)
            {
                foreach (var member in tds.Members)
                {
                    if (member is MethodDeclarationSyntax methodSyntax
                        && methodSyntax.Identifier.Text == "Configure"
                        && methodSyntax.Body is not null)
                    {
                        ParseCrossFieldRules(methodSyntax.Body, context.SemanticModel, crossFieldRules, properties, symbol, conditionalRules, ruleSetRules);
                        break;
                    }
                }
            }

            if (properties.Count == 0 && crossFieldRules.Count == 0 && nestedProps.Count == 0 && conditionalRules.Count == 0 && ruleSetRules.Count == 0) return null;

            var hintName = symbol.ToDisplayString().Replace(".", "_").Replace("<", "_").Replace(">", "_");

            bool isPartial = symbol.DeclaringSyntaxReferences
                .Any(r => r.GetSyntax() is TypeDeclarationSyntax tds2
                    && tds2.Modifiers.Any(SyntaxKind.PartialKeyword));

            var info = new ValidationInfo(
                symbol.Name, ns, hintName, isRecord, properties, isPartial);
            info.CrossFieldRules.AddRange(crossFieldRules);
            info.NestedProperties.AddRange(nestedProps);
            info.ConditionalRules.AddRange(conditionalRules);
            info.RuleSetRules.AddRange(ruleSetRules);
            return info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses cross-field rules from IValidationConfigurator.Configure() body.
    /// Supports b.Rule(x => expr, "msg"), b.Property(x => x.Prop).GreaterThan(x => x.Other),
    /// and b.When(x => condition, then => { ... }).
    /// </summary>
    private static void ParseCrossFieldRules(BlockSyntax body, SemanticModel sm, List<CrossFieldRule> rules, List<PropertyValidationInfo>? fluentProperties = null, INamedTypeSymbol? typeSymbol = null, List<ConditionalRule>? conditionalRules = null, List<RuleSetRule>? ruleSetRules = null)
    {
        foreach (var stmt in body.Statements)
        {
            if (stmt is not ExpressionStatementSyntax es) continue;

            // Unwrap the call chain
            var expr = es.Expression;

            // Try b.RuleSet("name", set => { ... })
            if (expr is InvocationExpressionSyntax rsInv
                && rsInv.Expression is MemberAccessExpressionSyntax rsMae
                && rsMae.Name.Identifier.Text == "RuleSet"
                && rsInv.ArgumentList.Arguments.Count == 2
                && ruleSetRules != null)
            {
                var nameArg = rsInv.ArgumentList.Arguments[0].Expression;
                var bodyArg = rsInv.ArgumentList.Arguments[1].Expression;

                var nameVal = sm.GetConstantValue(nameArg);
                if (nameVal.HasValue && nameVal.Value is string rsName && bodyArg is SimpleLambdaExpressionSyntax rsLambda)
                {
                    var innerRules = new List<CrossFieldRule>();
                    if (rsLambda.Body is BlockSyntax innerBlock)
                    {
                        ParseCrossFieldRules(innerBlock, sm, innerRules, fluentProperties, typeSymbol, null, null);
                    }

                    if (innerRules.Count > 0)
                    {
                        ruleSetRules.Add(new RuleSetRule
                        {
                            Name = rsName,
                            InnerRules = innerRules,
                        });
                    }
                }
                continue;
            }

            // Try b.When(x => condition, then => { ... }) or b.Unless(x => condition, then => { ... })
            if (expr is InvocationExpressionSyntax whenInv
                && whenInv.Expression is MemberAccessExpressionSyntax whenMae
                && whenMae.Name.Identifier.Text is "When" or "Unless"
                && whenInv.ArgumentList.Arguments.Count == 2
                && conditionalRules != null)
            {
                var condArg = whenInv.ArgumentList.Arguments[0].Expression;
                var thenArg = whenInv.ArgumentList.Arguments[1].Expression;

                if (condArg is SimpleLambdaExpressionSyntax condLambda && thenArg is SimpleLambdaExpressionSyntax thenLambda)
                {
                    var isUnless = whenMae.Name.Identifier.Text == "Unless";
                    var paramName = condLambda.Parameter.Identifier.Text;
                    var condText = condLambda.Body.ToString();
                    condText = System.Text.RegularExpressions.Regex.Replace(
                        condText, $@"\b{paramName}\.", "");
                    // Unless = negate the condition
                    if (isUnless)
                        condText = $"!({condText})";

                    var innerRules = new List<CrossFieldRule>();

                    // Parse inner body: then => { then.Rule(...); }
                    if (thenLambda.Body is BlockSyntax innerBlock)
                    {
                        ParseCrossFieldRules(innerBlock, sm, innerRules, fluentProperties, typeSymbol, null);
                    }

                    if (innerRules.Count > 0)
                    {
                        conditionalRules.Add(new ConditionalRule
                        {
                            ConditionExpression = condText,
                            InnerRules = innerRules,
                        });
                    }
                }
                continue;
            }

            // Try b.Rule(x => ..., "msg")
            if (expr is InvocationExpressionSyntax ruleInv
                && ruleInv.Expression is MemberAccessExpressionSyntax ruleMae
                && ruleMae.Name.Identifier.Text == "Rule"
                && ruleInv.ArgumentList.Arguments.Count == 2)
            {
                var lambdaArg = ruleInv.ArgumentList.Arguments[0].Expression;
                var msgArg = ruleInv.ArgumentList.Arguments[1].Expression;
                var msgVal = sm.GetConstantValue(msgArg);
                if (msgVal.HasValue && msgVal.Value is string msg && lambdaArg is SimpleLambdaExpressionSyntax lambda)
                {
                    var paramName = lambda.Parameter.Identifier.Text;
                    var exprText = lambda.Body.ToString();
                    // Replace parameter references (x.Prop -> Prop) for use inside the class
                    exprText = System.Text.RegularExpressions.Regex.Replace(
                        exprText, $@"\b{paramName}\.", "");
                    rules.Add(new CrossFieldRule
                    {
                        Kind = CrossFieldRuleKind.Expression,
                        ExpressionText = exprText,
                        Message = msg,
                    });
                }
                continue;
            }

            // Try b.Property(x => x.Left).GreaterThan(x => x.Right, "msg")
            ParsePropertyChain(expr, sm, rules, fluentProperties, typeSymbol);
        }
    }

    private static void ParsePropertyChain(ExpressionSyntax expr, SemanticModel sm, List<CrossFieldRule> rules, List<PropertyValidationInfo>? fluentProperties = null, INamedTypeSymbol? typeSymbol = null)
    {
        // Collect chain calls in order
        var calls = new List<(string Method, InvocationExpressionSyntax Inv)>();
        var current = expr;
        while (current is InvocationExpressionSyntax inv
               && inv.Expression is MemberAccessExpressionSyntax mae)
        {
            calls.Add((mae.Name.Identifier.Text, inv));
            current = mae.Expression;
        }

        // Reverse: innermost call (Property) first, then comparisons
        calls.Reverse();
        string? leftProp = null;
        foreach (var (method, inv) in calls)
        {
            if (method == "Property" && inv.ArgumentList.Arguments.Count >= 1)
            {
                leftProp = ExtractPropertyName(inv.ArgumentList.Arguments[0].Expression);
            }
            else if (leftProp != null && IsComparisonMethod(method) && inv.ArgumentList.Arguments.Count >= 1)
            {
                var rightProp = ExtractPropertyName(inv.ArgumentList.Arguments[0].Expression);
                if (rightProp == null) continue;

                string? msg = null;
                if (inv.ArgumentList.Arguments.Count >= 2)
                {
                    var msgVal = sm.GetConstantValue(inv.ArgumentList.Arguments[1].Expression);
                    if (msgVal.HasValue) msg = msgVal.Value as string;
                }
                msg ??= $"{leftProp} must be {FormatComparison(method)} {rightProp}.";

                rules.Add(new CrossFieldRule
                {
                    Kind = method switch
                    {
                        "GreaterThan" => CrossFieldRuleKind.GreaterThan,
                        "GreaterThanOrEqual" => CrossFieldRuleKind.GreaterThanOrEqual,
                        "LessThan" => CrossFieldRuleKind.LessThan,
                        "LessThanOrEqual" => CrossFieldRuleKind.LessThanOrEqual,
                        "EqualTo" => CrossFieldRuleKind.EqualTo,
                        "NotEqualTo" => CrossFieldRuleKind.NotEqualTo,
                        _ => CrossFieldRuleKind.Expression,
                    },
                    LeftProperty = leftProp,
                    RightProperty = rightProp,
                    Message = msg,
                });
            }
            else if (leftProp != null && IsFluentValidationMethod(method) && fluentProperties != null)
            {
                // Per-property fluent rules: .Required(), .Email(), .MinLength(n), etc.
                var args = inv.ArgumentList.Arguments;

                ValidationRule? rule = method switch
                {
                    "Required" => new ValidationRule(ValidationRuleKind.Required, GetOptionalMessage(args, 0, sm)),
                    "Email" => new ValidationRule(ValidationRuleKind.Email, GetOptionalMessage(args, 0, sm)),
                    "Url" => new ValidationRule(ValidationRuleKind.Url, GetOptionalMessage(args, 0, sm)),
                    "NotEmpty" => new ValidationRule(ValidationRuleKind.NotEmpty, GetOptionalMessage(args, 0, sm)),
                    "MinLength" when args.Count >= 1 && sm.GetConstantValue(args[0].Expression) is { HasValue: true, Value: int minLen }
                        => new ValidationRule(ValidationRuleKind.MinLength, GetOptionalMessage(args, 1, sm), minValue: minLen),
                    "MaxLength" when args.Count >= 1 && sm.GetConstantValue(args[0].Expression) is { HasValue: true, Value: int maxLen }
                        => new ValidationRule(ValidationRuleKind.MaxLength, GetOptionalMessage(args, 1, sm), maxValue: maxLen),
                    "Range" when args.Count >= 2
                        && sm.GetConstantValue(args[0].Expression) is { HasValue: true, Value: var minV }
                        && sm.GetConstantValue(args[1].Expression) is { HasValue: true, Value: var maxV }
                        => new ValidationRule(ValidationRuleKind.Range, GetOptionalMessage(args, 2, sm),
                            minValue: System.Convert.ToDouble(minV), maxValue: System.Convert.ToDouble(maxV)),
                    "Match" when args.Count >= 1 && sm.GetConstantValue(args[0].Expression) is { HasValue: true, Value: string pattern }
                        => new ValidationRule(ValidationRuleKind.Match, GetOptionalMessage(args, 1, sm), pattern: pattern),
                    "CreditCard" => new ValidationRule(ValidationRuleKind.CreditCard, GetOptionalMessage(args, 0, sm)),
                    "Phone" => new ValidationRule(ValidationRuleKind.Phone, GetOptionalMessage(args, 0, sm)),
                    "In" when args.Count >= 1 => new ValidationRule(ValidationRuleKind.In, null, allowedValues: ExtractStringArgs(args, sm)),
                    "NotIn" when args.Count >= 1 => new ValidationRule(ValidationRuleKind.NotIn, null, allowedValues: ExtractStringArgs(args, sm)),
                    _ => null,
                };

                if (rule != null)
                {
                    var existing = fluentProperties.Find(p => p.PropertyName == leftProp);
                    if (existing != null)
                    {
                        existing.Rules.Add(rule);
                    }
                    else
                    {
                        var propSymbol = typeSymbol?.GetMembers(leftProp).OfType<IPropertySymbol>().FirstOrDefault();
                        var typeName = propSymbol?.Type.ToDisplayString() ?? "object";
                        var isNullable = propSymbol?.Type.NullableAnnotation == NullableAnnotation.Annotated;
                        var isValue = propSymbol?.Type.IsValueType ?? false;
                        var propInfo = new PropertyValidationInfo(leftProp, typeName, isNullable, isValue, new List<ValidationRule> { rule });
                        fluentProperties.Add(propInfo);
                    }
                }
            }
        }
    }

    private static string? GetOptionalMessage(SeparatedSyntaxList<ArgumentSyntax> args, int index, SemanticModel sm)
    {
        if (args.Count <= index) return null;
        var cv = sm.GetConstantValue(args[index].Expression);
        return cv.HasValue ? cv.Value as string : null;
    }

    private static bool IsFluentValidationMethod(string name)
        => name is "Required" or "Email" or "Url" or "NotEmpty" or "MinLength" or "MaxLength" or "Range" or "Match"
            or "CreditCard" or "Phone" or "In" or "NotIn";

    private static string[]? ExtractStringArgs(SeparatedSyntaxList<ArgumentSyntax> args, SemanticModel sm)
    {
        var values = new List<string>();
        foreach (var arg in args)
        {
            var cv = sm.GetConstantValue(arg.Expression);
            if (cv.HasValue && cv.Value is string s)
                values.Add(s);
        }
        return values.Count > 0 ? values.ToArray() : null;
    }

    private static string? ExtractPropertyName(ExpressionSyntax expr)
    {
        if (expr is not SimpleLambdaExpressionSyntax lambda) return null;
        if (lambda.Body is MemberAccessExpressionSyntax mae)
            return mae.Name.Identifier.Text;
        return null;
    }

    private static bool IsComparisonMethod(string name)
        => name is "GreaterThan" or "GreaterThanOrEqual" or "LessThan" or "LessThanOrEqual" or "EqualTo" or "NotEqualTo";

    private static string FormatComparison(string method) => method switch
    {
        "GreaterThan" => "greater than",
        "GreaterThanOrEqual" => "greater than or equal to",
        "LessThan" => "less than",
        "LessThanOrEqual" => "less than or equal to",
        "EqualTo" => "equal to",
        "NotEqualTo" => "not equal to",
        _ => method,
    };

    private static ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
            return array.ElementType;
        if (type is INamedTypeSymbol named)
        {
            foreach (var iface in named.AllInterfaces)
            {
                if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                    && iface.TypeArguments.Length == 1)
                    return iface.TypeArguments[0];
            }
            if (named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>"
                && named.TypeArguments.Length == 1)
                return named.TypeArguments[0];
        }
        return null;
    }
}
