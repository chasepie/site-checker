using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Reinforced.Typings;
using Reinforced.Typings.Ast;
using Reinforced.Typings.Ast.TypeNames;
using Reinforced.Typings.Generators;

namespace SiteChecker.Backend.Generators;

public class ControllerGenerator : ClassCodeGenerator
{
    private const string _httpClient = nameof(_httpClient);

    private static void FixMethodName(RtFunction method)
    {
        var origName = method.Identifier.IdentifierName;
        method.Identifier.IdentifierName = string.Concat(
            origName[..1].ToLowerInvariant(),
            origName.AsSpan(1));
    }

    private static string GetMethodUrl(RtFunction func, Type classType)
    {
        var classRouteAttr = classType.GetCustomAttribute<RouteAttribute>()!;
        var method = classType.GetMethod(func.Identifier.IdentifierName)!;
        var methodHttpAttr = method.GetCustomAttribute<HttpMethodAttribute>()!;
        return $"{classRouteAttr.Template}/{methodHttpAttr.Template}"
            .Replace("[controller]", classType.Name.Replace("Controller", string.Empty))
            .Replace("{", "${")
            .TrimEnd('/');
    }

    private static string GetReturnType(RtFunction func, Type classType, TypeResolver typeResolver)
    {
        var method = classType.GetMethod(func.Identifier.IdentifierName)!;
        var returnType = method.ReturnType;
        var typeName = string.Empty;

        while (returnType.Name.Contains(nameof(Task))
            || (
                returnType.FullName?.Contains(nameof(ActionResult)) == true
                && returnType.IsGenericType
            ))
        {
            returnType = returnType.GetGenericArguments()[0];
            typeName = typeResolver.ResolveTypeName(returnType).ToString() ?? "any";
        }

        return typeName;
    }

    private static string GetHttpMethod(RtFunction func, Type classType)
    {
        var method = classType.GetMethod(func.Identifier.IdentifierName)!;
        var methodHttpAttr = method.GetCustomAttribute<HttpMethodAttribute>()!;
        return methodHttpAttr.HttpMethods.First().ToUpperInvariant();
    }

    private static string BuildQueryParams(RtFunction func, Type classType)
    {
        var method = classType.GetMethod(func.Identifier.IdentifierName)!;
        var parameters = method.GetParameters()
            .Where(p => p.GetCustomAttribute<FromQueryAttribute>() != null)
            .ToList();

        if (parameters.Count == 0)
        {
            return "{}";
        }

        var sb = new StringBuilder();
        sb.Append("{ ");
        sb.Append(string.Join(", ", parameters.Select(p => p.Name)));
        sb.Append(" }");
        return sb.ToString();
    }

    private static string BuildBodyParam(RtFunction func, Type classType)
    {
        var method = classType.GetMethod(func.Identifier.IdentifierName)!;
        var parameters = method.GetParameters()
            .Where(p => p.GetCustomAttribute<FromBodyAttribute>() != null)
            .ToList();

        if (parameters.Count == 0)
        {
            return "null";
        }

        return parameters.First().Name!;
    }

    private static void BuildMethodBody(RtFunction func, Type classType, TypeResolver resolver)
    {
        var methodName = GetMethodUrl(func, classType);

        var sb = new StringBuilder();
        sb.AppendLine($"const obs$ = this.{_httpClient}.request(");
        sb.AppendLine($"\t'{GetHttpMethod(func, classType)}',");
        sb.AppendLine($"\t`{methodName}`,");
        sb.AppendLine("\t{");
        sb.AppendLine($"\t\tparams: {BuildQueryParams(func, classType)},");
        sb.AppendLine($"\t\tbody: {BuildBodyParam(func, classType)}");
        sb.AppendLine("\t}");
        sb.AppendLine(");");
        sb.AppendLine("const result = await lastValueFrom(obs$);");

        var returnType = GetReturnType(func, classType, resolver)
            .Replace("[]", ".array()")
            .Replace("void", "z.void()")
            .Replace("<", "(")
            .Replace(">", ")");

        var returnPrefix = returnType.Contains("z.void()")
            ? string.Empty
            : "return ";
        sb.Append($"{returnPrefix}{returnType}.parse(result);");
        func.Body = new RtRaw(sb.ToString());
    }

    public override RtClass? GenerateNode(Type element, RtClass result, TypeResolver resolver)
    {
        var node = base.GenerateNode(element, result, resolver);
        if (node == null) return null;

        node.Decorators.Add(new RtDecorator("Injectable({ providedIn: 'root'})"));

        var members = node.Members.Prepend(
            new RtField()
            {
                Identifier = new RtIdentifier(_httpClient),
                Type = new RtSimpleTypeName("HttpClient"),
                InitializationExpression = "inject(HttpClient)",
                AccessModifier = AccessModifier.Private

            }
        ).ToList();

        foreach (var func in members.OfType<RtFunction>())
        {
            func.IsAsync = true;
            BuildMethodBody(func, element, resolver);
            FixMethodName(func);

            func.Arguments.RemoveAll(
                a => a.Identifier.IdentifierName == "cancellationToken"
            );
            func.ReturnType = null;
        }

        node.Members.Clear();
        node.Members.AddRange(members);
        return node;
    }
}
