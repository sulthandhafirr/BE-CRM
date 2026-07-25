using CRM.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using CRM.Api.Services;
using Npgsql;

LoadDotEnvFromKnownLocations();

var builder = WebApplication.CreateBuilder(args);

var supabaseUrl = builder.Configuration["Supabase:Url"];
var supabaseJwtSecret = builder.Configuration["Supabase:JwtSecret"]
    ?? throw new InvalidOperationException("Supabase JWT Secret is not configured");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });
});

// Add HttpClient for DeepSeek API
builder.Services.AddHttpClient();

var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection is not configured");

var dbConnectionBuilder = new NpgsqlConnectionStringBuilder(defaultConnectionString)
{
    // Supabase pooler + EF write bursts can invalidate pooled connectors in this app.
    Pooling = false,
    Multiplexing = false,
};

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(dbConnectionBuilder.ConnectionString,
        npgsqlOptions =>
        {
            npgsqlOptions.MaxBatchSize(1);
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorCodesToAdd: null);
            npgsqlOptions.CommandTimeout(60);
        }));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174", "https://capstone-crm.pages.dev")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"{supabaseUrl}/auth/v1";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            // IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(supabaseJwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseUrl}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true
        };
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"JWT Error: {context.Exception.Message}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddScoped<RoleService>();

builder.Services.AddScoped<EmailService>();

builder.Services.AddScoped<NotificationService>();

builder.Services.AddScoped<SentimentAnalysisService>();

builder.Services.AddScoped<IntentAnalysisService>();

builder.Services.AddScoped<UrgencyAnalysisService>();

builder.Services.AddScoped<PriorityEngineService>();

builder.Services.AddScoped<TicketRecommendationService>();

builder.Services.AddHostedService<SlaCheckerService>();

builder.Services.AddScoped<ChatToolService>();

builder.Services.AddScoped<CompanyService>();

builder.Services.AddScoped<RoleManagementService>();

var supabaseServiceKey = builder.Configuration["Supabase:ServiceKey"]
    ?? throw new InvalidOperationException("Supabase Service Key is not configured");

builder.Services.AddTransient(_ =>
    new Supabase.Client(supabaseUrl!, supabaseServiceKey, new Supabase.SupabaseOptions
    {
        AutoConnectRealtime = false
    })
);

var app = builder.Build();

// if (app.Environment.IsDevelopment())
// {
//     app.UseSwagger();
//     app.UseSwaggerUI();
// }

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static void LoadDotEnvFromKnownLocations()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env")),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".env"))
    };

    foreach (var path in candidates.Distinct())
    {
        if (LoadDotEnv(path))
        {
            return;
        }
    }
}

static bool LoadDotEnv(string filePath)
{
    if (!File.Exists(filePath))
    {
        return false;
    }

    foreach (var rawLine in File.ReadAllLines(filePath))
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
        {
            continue;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            continue;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim().Trim('"');

        if (!string.IsNullOrWhiteSpace(key))
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    return true;
}