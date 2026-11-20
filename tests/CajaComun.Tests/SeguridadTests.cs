using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Seguridad;
using CajaComun.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CajaComun.Tests;

public class TokenServiceTests
{
    internal const string Secreto = "secreto-de-pruebas-con-mas-de-32-caracteres";

    private static TokenService Servicio() => new(new OpcionesJwt { Secreto = Secreto, MinutosAcceso = 15 });

    private static readonly Usuario Mariana = new()
    {
        Id = 2, Nombre = "Mariana Treviño Paz", Correo = "mariana@ejemplo.mx", Rol = RolUsuario.Capturista
    };

    [Fact]
    public async Task El_token_de_acceso_lleva_usuario_y_rol_y_se_valida_con_la_misma_llave()
    {
        var (token, segundos) = Servicio().CrearAcceso(Mariana, DateTime.UtcNow);
        Assert.Equal(900, segundos);

        var r = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = OpcionesJwt.Emisor, ValidAudience = OpcionesJwt.Audiencia,
            IssuerSigningKey = new OpcionesJwt { Secreto = Secreto }.Llave
        });
        Assert.True(r.IsValid);
        Assert.Equal("2", r.Claims["sub"]);
        Assert.Equal("Capturista", r.Claims["role"]);
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_llave_no_es_valido()
    {
        var ajeno = new TokenService(new OpcionesJwt { Secreto = "otro-secreto-distinto-de-32-caracteres-o-mas" });
        var (token, _) = ajeno.CrearAcceso(Mariana, DateTime.UtcNow);
        var r = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = OpcionesJwt.Emisor, ValidAudience = OpcionesJwt.Audiencia,
            IssuerSigningKey = new OpcionesJwt { Secreto = Secreto }.Llave
        });
        Assert.False(r.IsValid);
    }

    [Fact]
    public void El_refresh_es_aleatorio_y_solo_se_guarda_su_hash()
    {
        var s = Servicio();
        var ahora = new DateTime(2024, 10, 31, 12, 0, 0, DateTimeKind.Utc);
        var a = s.CrearRefresh(ahora);
        var b = s.CrearRefresh(ahora);

        Assert.NotEqual(a.Token, b.Token);
        Assert.Equal(TokenService.Hash(a.Token), a.Hash);
        Assert.Equal(64, a.Hash.Length);
        Assert.DoesNotContain(a.Token, a.Hash);
        Assert.Equal(ahora.AddDays(7), a.Expira);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corto")]
    public void Sin_secreto_suficiente_la_configuracion_falla(string? secreto)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(secreto is null ? [] : new Dictionary<string, string?> { ["Jwt:Secret"] = secreto })
            .Build();
        Assert.Throws<InvalidOperationException>(() => OpcionesJwt.Desde(config));
    }
}

public class CajaAccesoTests
{
    private sealed record Actual(int Id, RolUsuario Rol, bool EstaAutenticado = true) : IUsuarioActual;

    private static CajaComunDbContext NuevoContexto()
    {
        var db = new CajaComunDbContext(new DbContextOptionsBuilder<CajaComunDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var empresa = new Empresa { Id = 1, Nombre = "Empresa" };
        db.Empresas.Add(empresa);
        db.Cajas.AddRange(
            new Caja { Id = 1, Empresa = empresa, Nombre = "GIMSA", Prefijo = "GIM", TasaInteresMensual = 1.5m },
            new Caja { Id = 2, Empresa = empresa, Nombre = "Aceros", Prefijo = "ACE", TasaInteresMensual = 1.25m },
            new Caja { Id = 3, Empresa = empresa, Nombre = "Transportes", Prefijo = "TDB", TasaInteresMensual = 1.75m });
        db.Usuarios.AddRange(
            new Usuario { Id = 1, Nombre = "Admin", Correo = "a@x.mx", Rol = RolUsuario.Administrador, AccesoTodasCajas = true },
            new Usuario { Id = 2, Nombre = "Capturista", Correo = "c@x.mx", Rol = RolUsuario.Capturista },
            new Usuario { Id = 3, Nombre = "Sin cajas", Correo = "s@x.mx", Rol = RolUsuario.Consulta },
            new Usuario { Id = 4, Nombre = "Baja", Correo = "b@x.mx", Rol = RolUsuario.Consulta, Activo = false, AccesoTodasCajas = true });
        db.UsuarioCajas.AddRange(new UsuarioCaja { UsuarioId = 2, CajaId = 1 }, new UsuarioCaja { UsuarioId = 2, CajaId = 3 });
        db.SaveChanges();
        return db;
    }

    private static CajaAcceso Acceso(CajaComunDbContext db, int usuarioId, RolUsuario rol) =>
        new(db, new Actual(usuarioId, rol));

    [Fact]
    public async Task El_administrador_con_acceso_global_ve_todas_las_cajas()
    {
        using var db = NuevoContexto();
        Assert.Equal([1, 2, 3], (await Acceso(db, 1, RolUsuario.Administrador).CajasPermitidasAsync()).Order());
    }

    [Fact]
    public async Task Un_usuario_solo_ve_las_cajas_que_tiene_asignadas()
    {
        using var db = NuevoContexto();
        Assert.Equal([1, 3], (await Acceso(db, 2, RolUsuario.Capturista).CajasPermitidasAsync()).Order());
    }

    [Fact]
    public async Task Pedir_una_caja_ajena_responde_403_con_codigo_estable()
    {
        using var db = NuevoContexto();
        var acceso = Acceso(db, 2, RolUsuario.Capturista);

        await acceso.ExigirAsync(1);
        var error = await Assert.ThrowsAsync<ErrorNegocio>(() => acceso.ExigirAsync(2));
        Assert.Equal(Codigos.CajaSinAcceso, error.Codigo);
        Assert.Equal(403, error.Estado);
    }

    [Fact]
    public async Task Sin_asignaciones_no_se_ve_ninguna_caja()
    {
        using var db = NuevoContexto();
        Assert.Empty(await Acceso(db, 3, RolUsuario.Consulta).CajasPermitidasAsync());
    }

    [Fact]
    public async Task Un_usuario_desactivado_no_ve_nada_aunque_tuviera_acceso_global()
    {
        using var db = NuevoContexto();
        Assert.Empty(await Acceso(db, 4, RolUsuario.Consulta).CajasPermitidasAsync());
    }

    [Fact]
    public async Task Quitar_una_caja_surte_efecto_en_la_siguiente_peticion()
    {
        using var db = NuevoContexto();
        Assert.Contains(3, await Acceso(db, 2, RolUsuario.Capturista).CajasPermitidasAsync());

        db.UsuarioCajas.Remove(await db.UsuarioCajas.FirstAsync(x => x.UsuarioId == 2 && x.CajaId == 3));
        await db.SaveChangesAsync();

        Assert.DoesNotContain(3, await Acceso(db, 2, RolUsuario.Capturista).CajasPermitidasAsync());
    }
}
