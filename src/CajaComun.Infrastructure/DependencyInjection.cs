using CajaComun.Application.AhorroVoluntario;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Auth;
using CajaComun.Application.Ahorradores;
using CajaComun.Application.Cajas;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using CajaComun.Application.Movimientos;
using CajaComun.Application.Nomina;
using CajaComun.Application.Prestamos;
using CajaComun.Application.Reportes;
using CajaComun.Application.Tareas;
using CajaComun.Application.Trabajos;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Application.Consultas;
using CajaComun.Infrastructure.Cache;
using CajaComun.Infrastructure.Consultas;
using CajaComun.Infrastructure.Tareas;
using CajaComun.Infrastructure.Trabajos;
using CajaComun.Application.Usuarios;
using CajaComun.Infrastructure.Health;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Seguridad;
using CajaComun.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Quartz;
using StackExchange.Redis;

namespace CajaComun.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var mariaDb = MariaDbConnection.Build(config);
        var redisCs = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Redis");

        services.AddDbContext<CajaComunDbContext>(o =>
            o.UseMySql(mariaDb, new MariaDbServerVersion(new Version(11, 4, 0))));
        services.AddSingleton<IFabricaConexion>(new FabricaConexion(mariaDb));
        services.AddSingleton(TimeProvider.System);

        // abortConnect=false: la API arranca aunque Redis tarde; el health lo reporta.
        var redisOptions = ConfigurationOptions.Parse(redisCs);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        var jwt = OpcionesJwt.Desde(config);
        services.AddSingleton(jwt);
        services.AddSingleton<TokenService>();

        services.AddScoped<ICajaAcceso, CajaAcceso>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICajaService, CajaService>();
        services.AddScoped<IAhorradorService, AhorradorService>();
        services.AddScoped<IMovimientoService, MovimientoService>();
        services.AddScoped<IUsuarioService, UsuarioService>();
        services.AddScoped<INominaService, NominaService>();
        services.AddScoped<IImportacionService, ImportacionService>();
        services.AddScoped<IPrestamoService, PrestamoService>();
        services.AddScoped<IAhorroVoluntarioService, AhorroVoluntarioService>();

        // Ciclos, reportes y panel
        services.AddSingleton<ILayoutBancario, LayoutSpeiGenerico>();
        services.AddSingleton<ILayoutBancario, LayoutSpeiBanorteSimplificado>();
        services.AddScoped<ICicloService, CicloService>();
        services.AddScoped<IReporteService, ReporteService>();
        services.AddScoped<IDashboardService, DashboardService>();

        // Caché y trabajos en segundo plano (el estado y los archivos de los trabajos viven en Redis)
        services.AddSingleton<ICacheDatos, CacheRedis>();
        services.AddScoped<ContextoTrabajo>();
        services.AddSingleton<IAlmacenTrabajos, AlmacenTrabajosRedis>();
        services.AddSingleton<ColaTrabajos>();
        services.AddSingleton<IColaTrabajos>(sp => sp.GetRequiredService<ColaTrabajos>());
        services.AddHostedService<ProcesadorTrabajos>();

        // Tareas programadas: la configuración está en la base y Quartz se reprograma en caliente. Con
        // Tareas:Habilitadas=false (pruebas, comandos) no se arranca el programador.
        if (config.GetValue("Tareas:Habilitadas", true))
        {
            services.AddQuartz();
            services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
            services.AddSingleton<ProgramadorTareas>();
            services.AddSingleton<IProgramadorTareas>(sp => sp.GetRequiredService<ProgramadorTareas>());
            services.AddHostedService<ArranqueTareas>();
        }
        else services.AddSingleton<IProgramadorTareas, ProgramadorInactivo>();
        services.AddScoped<IEjecutorTareas, EjecutorTareas>();
        services.AddScoped<ITareaService, TareaService>();

        // Parte 2: consultas distribuidas con Trino. El cliente habla el protocolo REST; el timeout total lo controla él
        // mismo (por eso el del HttpClient es infinito) y Polly reintenta solo las lecturas.
        services.AddSingleton(new OpcionesTrino
        {
            Url = config["Trino:Url"] ?? "http://localhost:8081",
            Usuario = config["Trino:Usuario"] ?? "caja-comun",
        });
        // ConnectTimeout corto: con la federación apagada hay que fallar en segundos, no esperar el límite de la consulta.
        services.AddHttpClient<ITrinoClient, TrinoClient>(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(3) });
        services.AddScoped<MotorConsultas>();
        services.AddScoped<IConsultasDistribuidasService, ConsultasDistribuidasService>();
        services.AddTransient<ICargaFederacion, CargaFederacion>();

        services.AddHealthChecks()
            .AddCheck("mariadb", new MariaDbHealthCheck(mariaDb), tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }
}
