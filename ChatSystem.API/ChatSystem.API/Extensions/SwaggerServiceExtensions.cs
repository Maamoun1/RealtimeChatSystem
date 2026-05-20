using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;

namespace ChatSystem.API.Extensions;

public static class SwaggerServiceExtensions
{
    public static IServiceCollection AddSwaggerDocumentation(
        this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "RealtimeChatSystem API",
                Version = "v1",
                Description = "Production-grade real-time chat backend — " +
                              "Clean Architecture · SignalR · JWT · Redis · RabbitMQ"
            });

            // ── JWT security definition ───────────────────────────────────────
            // Tells Swagger UI: "there is a Bearer token scheme"
            var jwtScheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Description = "Enter: Bearer {your JWT token}",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = JwtBearerDefaults.AuthenticationScheme,
                BearerFormat = "JWT",
                Reference = new OpenApiReference
                {
                    Id = JwtBearerDefaults.AuthenticationScheme,
                    Type = ReferenceType.SecurityScheme
                }
            };

            options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, jwtScheme);

            // Apply the JWT requirement globally — every endpoint shows the lock icon.
            // Individual endpoints can opt out with [AllowAnonymous].
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                { jwtScheme, Array.Empty<string>() }
            });

            // Include XML comments if the project generates them.
            // Requires <GenerateDocumentationFile>true</GenerateDocumentationFile> in .csproj
            var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                options.IncludeXmlComments(xmlPath);
        });

        return services;
    }

    public static IApplicationBuilder UseSwaggerDocumentation(
        this IApplicationBuilder app,
        IWebHostEnvironment env)
    {
        // Swagger UI in Development only.
        // In production, documentation should be behind auth or entirely disabled.
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "RealtimeChatSystem v1");
                options.RoutePrefix = "swagger"; // Serve at root: https://localhost:PORT/
            });
        }

        return app;
    }
}