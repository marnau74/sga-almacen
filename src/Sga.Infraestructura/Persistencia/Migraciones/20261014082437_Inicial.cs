using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sga.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalogo");

            migrationBuilder.EnsureSchema(
                name: "salidas");

            migrationBuilder.EnsureSchema(
                name: "inventario");

            migrationBuilder.EnsureSchema(
                name: "entradas");

            migrationBuilder.EnsureSchema(
                name: "stock");

            migrationBuilder.EnsureSchema(
                name: "almacen");

            migrationBuilder.EnsureSchema(
                name: "identidad");

            migrationBuilder.CreateTable(
                name: "articulo",
                schema: "catalogo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ean = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    marca = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    formato = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unidades_por_caja = table.Column<int>(type: "integer", nullable: false),
                    control_de_lote = table.Column<bool>(type: "boolean", nullable: false),
                    control_de_caducidad = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_articulo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cliente",
                schema: "catalogo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dias_minimos_de_vida_util = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cliente", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conteo",
                schema: "inventario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    abierto = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aprobado_por = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    ubicaciones = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conteo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lote_bloqueado",
                schema: "stock",
                columns: table => new
                {
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    desde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lote_bloqueado", x => new { x.articulo_id, x.lote });
                });

            migrationBuilder.CreateTable(
                name: "numerador",
                schema: "almacen",
                columns: table => new
                {
                    serie = table.Column<string>(type: "text", nullable: false),
                    ultimo = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_numerador", x => x.serie);
                });

            migrationBuilder.CreateTable(
                name: "oleada",
                schema: "salidas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lanzada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oleada", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "proveedor",
                schema: "catalogo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_proveedor", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reserva",
                schema: "stock",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linea = table.Column<int>(type: "integer", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    consumida = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reserva", x => x.id);
                    table.CheckConstraint("ck_reserva_cantidad", "cantidad > 0 AND consumida >= 0 AND consumida <= cantidad");
                });

            migrationBuilder.CreateTable(
                name: "rol",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tarea",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    hecho = table.Column<int>(type: "integer", nullable: false),
                    origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oleada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    operario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    creada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terminada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tarea", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuario",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    nombre_completo = table.Column<string>(type: "text", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ubicacion",
                schema: "almacen",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    zona = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pasillo = table.Column<int>(type: "integer", nullable: false),
                    modulo = table.Column<int>(type: "integer", nullable: false),
                    nivel = table.Column<int>(type: "integer", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    articulo_fijo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    minimo = table.Column<int>(type: "integer", nullable: true),
                    maximo = table.Column<int>(type: "integer", nullable: true),
                    bloqueada = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ubicacion", x => x.id);
                    table.CheckConstraint("ck_ubicacion_minimo_maximo", "minimo IS NULL OR (minimo >= 0 AND maximo > minimo)");
                    table.ForeignKey(
                        name: "fk_ubicacion_articulo_articulo_fijo_id",
                        column: x => x.articulo_fijo_id,
                        principalSchema: "catalogo",
                        principalTable: "articulo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedido",
                schema: "salidas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_de_servicio = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    oleada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expedido = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedido", x => x.id);
                    table.ForeignKey(
                        name: "fk_pedido_cliente_cliente_id",
                        column: x => x.cliente_id,
                        principalSchema: "catalogo",
                        principalTable: "cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "linea_de_conteo",
                schema: "inventario",
                columns: table => new
                {
                    conteo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    esperada = table.Column<int>(type: "integer", nullable: false),
                    primera = table.Column<int>(type: "integer", nullable: true),
                    segunda = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linea_de_conteo", x => new { x.conteo_id, x.id });
                    table.ForeignKey(
                        name: "fk_linea_de_conteo_conteo_conteo_id",
                        column: x => x.conteo_id,
                        principalSchema: "inventario",
                        principalTable: "conteo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recepcion",
                schema: "entradas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    proveedor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_prevista = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    observaciones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recepcion", x => x.id);
                    table.ForeignKey(
                        name: "fk_recepcion_proveedor_proveedor_id",
                        column: x => x.proveedor_id,
                        principalSchema: "catalogo",
                        principalTable: "proveedor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rol_claim",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_rol_claim_rol_role_id",
                        column: x => x.role_id,
                        principalSchema: "identidad",
                        principalTable: "rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_claim",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_claim", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuario_claim_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "identidad",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_login",
                schema: "identidad",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_usuario_login_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "identidad",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_rol",
                schema: "identidad",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    role_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_rol", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_usuario_rol_rol_role_id",
                        column: x => x.role_id,
                        principalSchema: "identidad",
                        principalTable: "rol",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_usuario_rol_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "identidad",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usuario_token",
                schema: "identidad",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuario_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_usuario_token_usuario_user_id",
                        column: x => x.user_id,
                        principalSchema: "identidad",
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "existencia",
                schema: "stock",
                columns: table => new
                {
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ubicacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    reservada = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_existencia", x => new { x.articulo_id, x.ubicacion_id, x.lote });
                    table.CheckConstraint("ck_existencia_cantidad", "cantidad >= 0");
                    table.CheckConstraint("ck_existencia_reservada", "reservada >= 0 AND reservada <= cantidad");
                    table.ForeignKey(
                        name: "fk_existencia_articulo_articulo_id",
                        column: x => x.articulo_id,
                        principalSchema: "catalogo",
                        principalTable: "articulo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_existencia_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalSchema: "almacen",
                        principalTable: "ubicacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "movimiento",
                schema: "stock",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    momento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    origen_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destino_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    usuario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movimiento", x => x.id);
                    table.CheckConstraint("ck_movimiento_cantidad", "cantidad > 0");
                    table.CheckConstraint("ck_movimiento_sitio", "origen_id IS NOT NULL OR destino_id IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_movimiento_articulo_articulo_id",
                        column: x => x.articulo_id,
                        principalSchema: "catalogo",
                        principalTable: "articulo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_ubicaciones_destino_id",
                        column: x => x.destino_id,
                        principalSchema: "almacen",
                        principalTable: "ubicacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_ubicaciones_origen_id",
                        column: x => x.origen_id,
                        principalSchema: "almacen",
                        principalTable: "ubicacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulto",
                schema: "salidas",
                columns: table => new
                {
                    numero = table.Column<int>(type: "integer", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sscc = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: false),
                    cerrado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulto", x => new { x.pedido_id, x.numero });
                    table.ForeignKey(
                        name: "fk_bulto_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalSchema: "salidas",
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "linea_de_pedido",
                schema: "salidas",
                columns: table => new
                {
                    numero = table.Column<int>(type: "integer", nullable: false),
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pedida = table.Column<int>(type: "integer", nullable: false),
                    asignada = table.Column<int>(type: "integer", nullable: false),
                    preparada = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linea_de_pedido", x => new { x.pedido_id, x.numero });
                    table.ForeignKey(
                        name: "fk_linea_de_pedido_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalSchema: "salidas",
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "preparado",
                schema: "salidas",
                columns: table => new
                {
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    embalado = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preparado", x => new { x.pedido_id, x.id });
                    table.ForeignKey(
                        name: "fk_preparado_pedido_pedido_id",
                        column: x => x.pedido_id,
                        principalSchema: "salidas",
                        principalTable: "pedido",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "descarga",
                schema: "entradas",
                columns: table => new
                {
                    numero = table.Column<int>(type: "integer", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    caducidad = table.Column<DateOnly>(type: "date", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    danado = table.Column<bool>(type: "boolean", nullable: false),
                    operario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    momento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_descarga", x => new { x.recepcion_id, x.numero });
                    table.ForeignKey(
                        name: "fk_descarga_recepcion_recepcion_id",
                        column: x => x.recepcion_id,
                        principalSchema: "entradas",
                        principalTable: "recepcion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "linea_de_recepcion",
                schema: "entradas",
                columns: table => new
                {
                    numero = table.Column<int>(type: "integer", nullable: false),
                    recepcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prevista = table.Column<int>(type: "integer", nullable: false),
                    recibida = table.Column<int>(type: "integer", nullable: false),
                    danada = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_linea_de_recepcion", x => new { x.recepcion_id, x.numero });
                    table.ForeignKey(
                        name: "fk_linea_de_recepcion_recepcion_recepcion_id",
                        column: x => x.recepcion_id,
                        principalSchema: "entradas",
                        principalTable: "recepcion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contenido_de_bulto",
                schema: "salidas",
                columns: table => new
                {
                    pedido_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulto_numero = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    articulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contenido_de_bulto", x => new { x.pedido_id, x.bulto_numero, x.id });
                    table.ForeignKey(
                        name: "fk_contenido_de_bulto_bulto_pedido_id_bulto_numero",
                        columns: x => new { x.pedido_id, x.bulto_numero },
                        principalSchema: "salidas",
                        principalTable: "bulto",
                        principalColumns: new[] { "pedido_id", "numero" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_articulo_ean",
                schema: "catalogo",
                table: "articulo",
                column: "ean",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bulto_sscc",
                schema: "salidas",
                table: "bulto",
                column: "sscc",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cliente_codigo",
                schema: "catalogo",
                table: "cliente",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conteo_numero",
                schema: "inventario",
                table: "conteo",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_existencia_ubicacion_id",
                schema: "stock",
                table: "existencia",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_articulo_id_lote",
                schema: "stock",
                table: "movimiento",
                columns: new[] { "articulo_id", "lote" });

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_destino_id",
                schema: "stock",
                table: "movimiento",
                column: "destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_documento",
                schema: "stock",
                table: "movimiento",
                column: "documento");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_momento",
                schema: "stock",
                table: "movimiento",
                column: "momento");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_origen_id",
                schema: "stock",
                table: "movimiento",
                column: "origen_id");

            migrationBuilder.CreateIndex(
                name: "ix_oleada_numero",
                schema: "salidas",
                table: "oleada",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pedido_cliente_id",
                schema: "salidas",
                table: "pedido",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_estado",
                schema: "salidas",
                table: "pedido",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "ix_pedido_numero",
                schema: "salidas",
                table: "pedido",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_proveedor_codigo",
                schema: "catalogo",
                table: "proveedor",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recepcion_numero",
                schema: "entradas",
                table: "recepcion",
                column: "numero",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recepcion_proveedor_id",
                schema: "entradas",
                table: "recepcion",
                column: "proveedor_id");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_articulo_id_ubicacion_id_lote",
                schema: "stock",
                table: "reserva",
                columns: new[] { "articulo_id", "ubicacion_id", "lote" },
                filter: "estado = 'Abierta'");

            migrationBuilder.CreateIndex(
                name: "ix_reserva_pedido_id",
                schema: "stock",
                table: "reserva",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "identidad",
                table: "rol",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rol_claim_role_id",
                schema: "identidad",
                table: "rol_claim",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarea_estado_tipo_orden",
                schema: "almacen",
                table: "tarea",
                columns: new[] { "estado", "tipo", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_tarea_oleada_id",
                schema: "almacen",
                table: "tarea",
                column: "oleada_id");

            migrationBuilder.CreateIndex(
                name: "ix_tarea_pedido_id",
                schema: "almacen",
                table: "tarea",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "ix_ubicacion_articulo_fijo_id",
                schema: "almacen",
                table: "ubicacion",
                column: "articulo_fijo_id");

            migrationBuilder.CreateIndex(
                name: "ix_ubicacion_codigo",
                schema: "almacen",
                table: "ubicacion",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identidad",
                table: "usuario",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identidad",
                table: "usuario",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_claim_user_id",
                schema: "identidad",
                table: "usuario_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_login_user_id",
                schema: "identidad",
                table: "usuario_login",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuario_rol_role_id",
                schema: "identidad",
                table: "usuario_rol",
                column: "role_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contenido_de_bulto",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "descarga",
                schema: "entradas");

            migrationBuilder.DropTable(
                name: "existencia",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "linea_de_conteo",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "linea_de_pedido",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "linea_de_recepcion",
                schema: "entradas");

            migrationBuilder.DropTable(
                name: "lote_bloqueado",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "movimiento",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "numerador",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "oleada",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "preparado",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "reserva",
                schema: "stock");

            migrationBuilder.DropTable(
                name: "rol_claim",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "tarea",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "usuario_claim",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_login",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_rol",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario_token",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "bulto",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "conteo",
                schema: "inventario");

            migrationBuilder.DropTable(
                name: "recepcion",
                schema: "entradas");

            migrationBuilder.DropTable(
                name: "ubicacion",
                schema: "almacen");

            migrationBuilder.DropTable(
                name: "rol",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "usuario",
                schema: "identidad");

            migrationBuilder.DropTable(
                name: "pedido",
                schema: "salidas");

            migrationBuilder.DropTable(
                name: "proveedor",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "articulo",
                schema: "catalogo");

            migrationBuilder.DropTable(
                name: "cliente",
                schema: "catalogo");
        }
    }
}
