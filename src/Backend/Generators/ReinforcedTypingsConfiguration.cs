using Microsoft.AspNetCore.Mvc;
using Reinforced.Typings.Ast.TypeNames;
using Reinforced.Typings.Fluent;
using SiteChecker.Backend.Services.VPN;
using SiteChecker.Database.Services;
using SiteChecker.Domain.DTOs;
using SiteChecker.Domain.Entities;
using SiteChecker.Domain.Enums;
using SiteChecker.Domain.ValueObjects;

namespace SiteChecker.Backend.Generators;

public static partial class ReinforcedTypingsConfiguration
{
    public static void Configure(Reinforced.Typings.Fluent.ConfigurationBuilder builder)
    {
        // fluent configuration goes here
        builder.Global(options => options
            .UseModules()
            .CamelCaseForProperties()
            .GenerateDocumentation());

        new List<Type>
        {
            typeof(byte[]),
            typeof(DateTime),
            typeof(long),
            typeof(TimeOnly),
            typeof(TimeSpan),
            typeof(ulong),
            typeof(Uri),
        }.ForEach(type => builder.Substitute(type, new RtSimpleTypeName("string")));

        builder
            .Substitute(typeof(object), new RtSimpleTypeName("object"))
            .Substitute(typeof(ActionResult), new RtSimpleTypeName("void"))
            .Substitute(typeof(IActionResult), new RtSimpleTypeName("void"));

        builder.AddImport("{ HttpClient }", "@angular/common/http");
        builder.AddImport("{ inject, Injectable }", "@angular/core");
        builder.AddImport("{ lastValueFrom }", "rxjs");
        builder.AddImport("* as z", "zod");

        // Enums
        var enums = new List<Type>
        {
            typeof(CheckStatus),
            typeof(PushoverPriority),
        };
        builder.ExportAsEnums(enums, builder =>
        {
            builder
                .UseString(true)
                .WithCodeGenerator<ZodEnumGenerator>();
        });

        // Classes/Interfaces
        var dbClasses = new List<Type>
        {
            typeof(PagedResponse<>),
            typeof(IEntityChange),
            typeof(EntityChange),
            typeof(CreatedEntityChange),
            typeof(UpdatedEntityChange),
            typeof(DeletedEntityChange),
            typeof(PiaLocation),
            typeof(DiscordConfig),
            typeof(IEntityWithId),
            typeof(PushoverConfig),
            typeof(SiteSchedule),
            typeof(SiteUpdate),
            typeof(SiteCheck),
            typeof(Site),
            typeof(SiteCheckScreenshot)
        };
        builder.ExportAsInterfaces(dbClasses, builder =>
        {
            builder
                .AutoI(false)
                .WithCodeGenerator<ZodGenerator>();
        });


        var assembly = typeof(ReinforcedTypingsConfiguration).Assembly;

        // API Controllers
        var controllers = assembly
            .GetTypes()
            .Where(t => t.IsClass
                && t.Namespace == "SiteChecker.Backend.Controllers"
                && t.BaseType == typeof(ControllerBase))
            .ToList();
        builder.ExportAsClasses(controllers, builder =>
        {
            builder
                .WithPublicMethods()
                .WithCodeGenerator<ControllerGenerator>();
        });

        // Constants
        List<Type> constants =
        [
            typeof(SignalRConstants)
        ];
        builder.ExportAsClasses(constants, builder =>
        {
            builder
                .WithPublicFields()
                .WithCodeGenerator<ConstGenerator>();
        });

        builder.TryLookupDocumentationForAssembly(assembly);
    }
}
