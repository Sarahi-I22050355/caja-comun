-- Caja Común - esquema de la base de datos (MariaDB 11, InnoDB, utf8mb4_unicode_ci).
-- Se exporta con scripts/exportar-schema.ps1 a partir de las migraciones; el sistema crea la base con
-- "dotnet ef database update" / "seed", este archivo es para leerla o cargarla por separado.
-- Las tablas van primero; después las vistas, la función y los procedimientos (db/objetos).

/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;
/*!40103 SET @OLD_TIME_ZONE=@@TIME_ZONE */;
/*!40103 SET TIME_ZONE='+00:00' */;
/*!40014 SET @OLD_UNIQUE_CHECKS=@@UNIQUE_CHECKS, UNIQUE_CHECKS=0 */;
/*!40014 SET @OLD_FOREIGN_KEY_CHECKS=@@FOREIGN_KEY_CHECKS, FOREIGN_KEY_CHECKS=0 */;
/*!40101 SET @OLD_SQL_MODE=@@SQL_MODE, SQL_MODE='NO_AUTO_VALUE_ON_ZERO' */;
/*M!100616 SET @OLD_NOTE_VERBOSITY=@@NOTE_VERBOSITY, NOTE_VERBOSITY=0 */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ahorrador` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `caja_id` int(11) NOT NULL,
  `num_empleado` varchar(20) NOT NULL,
  `nombre` varchar(200) NOT NULL,
  `apellidos` varchar(200) NOT NULL,
  `rfc` varchar(13) NOT NULL,
  `fecha_nacimiento` date NOT NULL,
  `fecha_ingreso` date NOT NULL,
  `fecha_baja` date DEFAULT NULL,
  `estado` enum('ACTIVO','LICENCIA','BAJA') NOT NULL,
  `sueldo_quincenal` decimal(14,2) NOT NULL,
  `clabe` char(18) NOT NULL,
  `departamento` varchar(200) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_ahorrador_caja_id_num_empleado` (`caja_id`,`num_empleado`),
  KEY `ix_ahorrador_caja_id_apellidos_nombre` (`caja_id`,`apellidos`,`nombre`),
  KEY `ix_ahorrador_caja_id_estado` (`caja_id`,`estado`),
  CONSTRAINT `fk_ahorrador_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `ck_ahorrador_clabe` CHECK (`clabe` regexp '^[0-9]{18}$'),
  CONSTRAINT `ck_ahorrador_sueldo` CHECK (`sueldo_quincenal` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `caja` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `empresa_id` int(11) NOT NULL,
  `nombre` varchar(200) NOT NULL,
  `prefijo` varchar(5) NOT NULL,
  `tasa_interes_mensual` decimal(5,2) NOT NULL,
  `frecuencia_nomina` enum('QUINCENAL','SEMANAL') NOT NULL,
  `inicio_ciclo` date NOT NULL,
  `fin_ciclo` date NOT NULL,
  `activa` tinyint(1) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_caja_empresa_id_nombre` (`empresa_id`,`nombre`),
  CONSTRAINT `fk_caja_empresa_id` FOREIGN KEY (`empresa_id`) REFERENCES `empresa` (`id`),
  CONSTRAINT `ck_caja_tasa` CHECK (`tasa_interes_mensual` between 1.00 and 2.00)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ciclo` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `caja_id` int(11) NOT NULL,
  `anio` int(11) NOT NULL,
  `estado` enum('ABIERTO','EN_CIERRE','CERRADO') NOT NULL,
  `interes_total` decimal(14,2) NOT NULL,
  `fecha_cierre` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_ciclo_caja_id_anio` (`caja_id`,`anio`),
  CONSTRAINT `fk_ciclo_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `ciclo_distribucion` (
  `ciclo_id` int(11) NOT NULL,
  `ahorrador_id` int(11) NOT NULL,
  `saldo_dias` decimal(16,2) NOT NULL,
  `saldo_promedio` decimal(14,2) NOT NULL,
  `dias_activos` int(11) NOT NULL,
  `participacion` decimal(10,6) NOT NULL,
  `interes_asignado` decimal(14,2) NOT NULL,
  `ahorro_liquidado` decimal(14,2) NOT NULL,
  `ahorro_retenido` decimal(14,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`ciclo_id`,`ahorrador_id`),
  KEY `ix_ciclo_distribucion_ahorrador_id` (`ahorrador_id`),
  CONSTRAINT `fk_ciclo_distribucion_ahorrador_id` FOREIGN KEY (`ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `fk_ciclo_distribucion_ciclo_id` FOREIGN KEY (`ciclo_id`) REFERENCES `ciclo` (`id`),
  CONSTRAINT `ck_distribucion_participacion` CHECK (`participacion` between 0 and 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `cuota_ahorro` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `ahorrador_id` int(11) NOT NULL,
  `cuota_ordinaria` decimal(14,2) NOT NULL,
  `cuota_voluntaria` decimal(14,2) NOT NULL,
  `voluntario_activo` tinyint(1) NOT NULL,
  `vigente_desde` date NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_cuota_ahorro_ahorrador_id_vigente_desde` (`ahorrador_id`,`vigente_desde`),
  CONSTRAINT `fk_cuota_ahorro_ahorrador_id` FOREIGN KEY (`ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `ck_cuota_montos` CHECK (`cuota_ordinaria` >= 0 and `cuota_voluntaria` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `empresa` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `nombre` varchar(200) NOT NULL,
  `activa` tinyint(1) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_empresa_nombre` (`nombre`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `importacion` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `caja_id` int(11) NOT NULL,
  `archivo` varchar(255) NOT NULL,
  `formato` enum('XLSX','XML','TXT','CSV') NOT NULL,
  `usuario_id` int(11) NOT NULL,
  `fecha` datetime(6) NOT NULL,
  `estado` enum('PREVIA','APLICADA','CANCELADA') NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_importacion_caja_id` (`caja_id`),
  KEY `ix_importacion_usuario_id` (`usuario_id`),
  CONSTRAINT `fk_importacion_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `fk_importacion_usuario_id` FOREIGN KEY (`usuario_id`) REFERENCES `usuario` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `importacion_detalle` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `importacion_id` int(11) NOT NULL,
  `numero_fila` int(11) NOT NULL,
  `tipo` enum('ALTA','BAJA','SIN_CAMBIO') NOT NULL,
  `num_empleado` varchar(20) NOT NULL,
  `datos` longtext NOT NULL,
  `validacion` enum('VALIDO','ERROR') NOT NULL,
  `mensaje_error` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_importacion_detalle_importacion_id` (`importacion_id`),
  CONSTRAINT `fk_importacion_detalle_importacion_id` FOREIGN KEY (`importacion_id`) REFERENCES `importacion` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `incidencia` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `ahorrador_id` int(11) NOT NULL,
  `periodo_id` int(11) NOT NULL,
  `tipo` enum('LICENCIA','INCAPACIDAD','BAJA','CAMBIO_CUOTA','SUSPENDE_VOLUNTARIO') NOT NULL,
  `valor` decimal(14,2) DEFAULT NULL,
  `nota` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_incidencia_ahorrador_id_periodo_id_tipo` (`ahorrador_id`,`periodo_id`,`tipo`),
  KEY `ix_incidencia_periodo_id` (`periodo_id`),
  CONSTRAINT `fk_incidencia_ahorrador_id` FOREIGN KEY (`ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `fk_incidencia_periodo_id` FOREIGN KEY (`periodo_id`) REFERENCES `periodo` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `interes_devengado` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `prestamo_id` int(11) NOT NULL,
  `periodo_id` int(11) NOT NULL,
  `fecha_corte` date NOT NULL,
  `saldo_insoluto` decimal(14,2) NOT NULL,
  `interes` decimal(14,2) NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_interes_devengado_prestamo_id_fecha_corte` (`prestamo_id`,`fecha_corte`),
  KEY `ix_interes_devengado_periodo_id` (`periodo_id`),
  CONSTRAINT `fk_interes_devengado_periodo_id` FOREIGN KEY (`periodo_id`) REFERENCES `periodo` (`id`),
  CONSTRAINT `fk_interes_devengado_prestamo_id` FOREIGN KEY (`prestamo_id`) REFERENCES `prestamo` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `layout_generado` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `tipo` enum('NOMINA','SPEI') NOT NULL,
  `caja_id` int(11) NOT NULL,
  `periodo_id` int(11) DEFAULT NULL,
  `ciclo_id` int(11) DEFAULT NULL,
  `formato` varchar(30) NOT NULL,
  `nombre_archivo` varchar(255) NOT NULL,
  `registros` int(11) NOT NULL,
  `importe_total` decimal(16,2) NOT NULL,
  `suma_control` varchar(64) DEFAULT NULL,
  `usuario_id` int(11) NOT NULL,
  `fecha` datetime(6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_layout_generado_caja_id` (`caja_id`),
  KEY `ix_layout_generado_ciclo_id` (`ciclo_id`),
  KEY `ix_layout_generado_periodo_id` (`periodo_id`),
  KEY `ix_layout_generado_usuario_id` (`usuario_id`),
  CONSTRAINT `fk_layout_generado_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `fk_layout_generado_ciclo_id` FOREIGN KEY (`ciclo_id`) REFERENCES `ciclo` (`id`),
  CONSTRAINT `fk_layout_generado_periodo_id` FOREIGN KEY (`periodo_id`) REFERENCES `periodo` (`id`),
  CONSTRAINT `fk_layout_generado_usuario_id` FOREIGN KEY (`usuario_id`) REFERENCES `usuario` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `movimiento` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `caja_id` int(11) NOT NULL,
  `ahorrador_id` int(11) NOT NULL,
  `periodo_id` int(11) DEFAULT NULL,
  `prestamo_id` int(11) DEFAULT NULL,
  `fecha` date NOT NULL,
  `tipo` enum('AHORRO_ORDINARIO','AHORRO_VOLUNTARIO','DESEMBOLSO_PRESTAMO','ABONO_CAPITAL','ABONO_INTERES','INTERES_ANUAL','LIQUIDACION_CICLO') NOT NULL,
  `monto` decimal(14,2) NOT NULL,
  `referencia` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_movimiento_periodo_id` (`periodo_id`),
  KEY `ix_movimiento_prestamo_id` (`prestamo_id`),
  KEY `ix_movimiento_ahorrador_id_fecha` (`ahorrador_id`,`fecha`),
  KEY `ix_movimiento_caja_id_periodo_id` (`caja_id`,`periodo_id`),
  KEY `ix_movimiento_tipo_fecha` (`tipo`,`fecha`),
  CONSTRAINT `fk_movimiento_ahorrador_id` FOREIGN KEY (`ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `fk_movimiento_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `fk_movimiento_periodo_id` FOREIGN KEY (`periodo_id`) REFERENCES `periodo` (`id`),
  CONSTRAINT `fk_movimiento_prestamo_id` FOREIGN KEY (`prestamo_id`) REFERENCES `prestamo` (`id`),
  CONSTRAINT `ck_movimiento_monto` CHECK (`monto` > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `periodo` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `caja_id` int(11) NOT NULL,
  `anio` int(11) NOT NULL,
  `numero` int(11) NOT NULL,
  `fecha_inicio` date NOT NULL,
  `fecha_fin` date NOT NULL,
  `estado` enum('ABIERTO','APLICADO','CERRADO') NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_periodo_caja_id_anio_numero` (`caja_id`,`anio`,`numero`),
  CONSTRAINT `fk_periodo_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `ck_periodo_fechas` CHECK (`fecha_fin` >= `fecha_inicio` and `numero` between 1 and 53)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `prestamo` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `folio` varchar(20) NOT NULL,
  `caja_id` int(11) NOT NULL,
  `ahorrador_id` int(11) NOT NULL,
  `monto` decimal(14,2) NOT NULL,
  `plazo_quincenas` int(11) NOT NULL,
  `tasa_mensual` decimal(5,2) NOT NULL,
  `garantia` enum('AHORRO_80','PAGARE_AVAL') NOT NULL,
  `aval_ahorrador_id` int(11) DEFAULT NULL,
  `estado` enum('PENDIENTE','VIGENTE','LIQUIDADO','RECHAZADO') NOT NULL,
  `fecha_solicitud` date NOT NULL,
  `fecha_autorizacion` date DEFAULT NULL,
  `autorizado_por` int(11) DEFAULT NULL,
  `excedente_aval` decimal(14,2) NOT NULL DEFAULT 0.00,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_prestamo_folio` (`folio`),
  KEY `ix_prestamo_ahorrador_id` (`ahorrador_id`),
  KEY `ix_prestamo_autorizado_por` (`autorizado_por`),
  KEY `ix_prestamo_aval_ahorrador_id` (`aval_ahorrador_id`),
  KEY `ix_prestamo_caja_id_estado` (`caja_id`,`estado`),
  CONSTRAINT `fk_prestamo_ahorrador_id` FOREIGN KEY (`ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `fk_prestamo_autorizado_por` FOREIGN KEY (`autorizado_por`) REFERENCES `usuario` (`id`),
  CONSTRAINT `fk_prestamo_aval_ahorrador_id` FOREIGN KEY (`aval_ahorrador_id`) REFERENCES `ahorrador` (`id`),
  CONSTRAINT `fk_prestamo_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `ck_prestamo_aval` CHECK (`garantia` <> 'PAGARE_AVAL' or `aval_ahorrador_id` is not null),
  CONSTRAINT `ck_prestamo_monto` CHECK (`monto` > 0 and `plazo_quincenas` > 0),
  CONSTRAINT `ck_prestamo_tasa` CHECK (`tasa_mensual` between 1.00 and 2.00),
  CONSTRAINT `ck_prestamo_excedente` CHECK (`excedente_aval` >= 0 and `excedente_aval` <= `monto`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `prestamo_amortizacion` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `prestamo_id` int(11) NOT NULL,
  `numero` int(11) NOT NULL,
  `periodo_id` int(11) NOT NULL,
  `fecha` date NOT NULL,
  `capital` decimal(14,2) NOT NULL,
  `interes` decimal(14,2) NOT NULL,
  `total` decimal(14,2) NOT NULL,
  `saldo_despues` decimal(14,2) NOT NULL,
  `estado` enum('PROYECTADO','PROGRAMADO','APLICADO') NOT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_prestamo_amortizacion_prestamo_id_numero` (`prestamo_id`,`numero`),
  KEY `ix_prestamo_amortizacion_periodo_id_estado` (`periodo_id`,`estado`),
  CONSTRAINT `fk_prestamo_amortizacion_periodo_id` FOREIGN KEY (`periodo_id`) REFERENCES `periodo` (`id`),
  CONSTRAINT `fk_prestamo_amortizacion_prestamo_id` FOREIGN KEY (`prestamo_id`) REFERENCES `prestamo` (`id`) ON DELETE CASCADE,
  CONSTRAINT `ck_amortizacion_montos` CHECK (`capital` >= 0 and `interes` >= 0 and `saldo_despues` >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `refresh_token` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `usuario_id` int(11) NOT NULL,
  `token_hash` varchar(64) NOT NULL,
  `creado` datetime(6) NOT NULL,
  `expira` datetime(6) NOT NULL,
  `revocado` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_refresh_token_token_hash` (`token_hash`),
  KEY `ix_refresh_token_usuario_id` (`usuario_id`),
  CONSTRAINT `fk_refresh_token_usuario_id` FOREIGN KEY (`usuario_id`) REFERENCES `usuario` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tarea_ejecucion` (
  `id` bigint(20) NOT NULL AUTO_INCREMENT,
  `tarea_id` int(11) NOT NULL,
  `inicio` datetime(6) NOT NULL,
  `fin` datetime(6) DEFAULT NULL,
  `estado` enum('EN_CURSO','EXITO','ERROR') NOT NULL,
  `resumen` varchar(500) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `ix_tarea_ejecucion_tarea_id` (`tarea_id`),
  CONSTRAINT `fk_tarea_ejecucion_tarea_id` FOREIGN KEY (`tarea_id`) REFERENCES `tarea_programada` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `tarea_programada` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `clave` varchar(40) NOT NULL,
  `nombre` varchar(200) NOT NULL,
  `frecuencia` enum('DIARIA','QUINCENAL','MENSUAL') NOT NULL,
  `hora` time(6) NOT NULL,
  `activa` tinyint(1) NOT NULL,
  `ultima_ejecucion` datetime(6) DEFAULT NULL,
  `proxima_ejecucion` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_tarea_programada_clave` (`clave`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `usuario` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `nombre` varchar(200) NOT NULL,
  `correo` varchar(200) NOT NULL,
  `hash_password` varchar(100) NOT NULL,
  `rol` enum('ADMINISTRADOR','CAPTURISTA','CONSULTA') NOT NULL,
  `acceso_todas_cajas` tinyint(1) NOT NULL,
  `activo` tinyint(1) NOT NULL,
  `ultimo_acceso` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ux_usuario_correo` (`correo`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40101 SET @saved_cs_client     = @@character_set_client */;
/*!40101 SET character_set_client = utf8mb4 */;
CREATE TABLE `usuario_caja` (
  `usuario_id` int(11) NOT NULL,
  `caja_id` int(11) NOT NULL,
  PRIMARY KEY (`usuario_id`,`caja_id`),
  KEY `ix_usuario_caja_caja_id` (`caja_id`),
  CONSTRAINT `fk_usuario_caja_caja_id` FOREIGN KEY (`caja_id`) REFERENCES `caja` (`id`),
  CONSTRAINT `fk_usuario_caja_usuario_id` FOREIGN KEY (`usuario_id`) REFERENCES `usuario` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
/*!40101 SET character_set_client = @saved_cs_client */;
/*!40103 SET TIME_ZONE=@OLD_TIME_ZONE */;

/*!40101 SET SQL_MODE=@OLD_SQL_MODE */;
/*!40014 SET FOREIGN_KEY_CHECKS=@OLD_FOREIGN_KEY_CHECKS */;
/*!40014 SET UNIQUE_CHECKS=@OLD_UNIQUE_CHECKS */;
/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
/*M!100616 SET NOTE_VERBOSITY=@OLD_NOTE_VERBOSITY */;


-- Objetos programables

DELIMITER ;;
-- Saldo de ahorro (ordinario + voluntario - liquidaciones) de un ahorrador a una fecha.
-- El rendimiento anual (INTERES_ANUAL) no forma parte del ahorro acumulado.
CREATE FUNCTION fn_saldo_ahorro(p_ahorrador_id INT, p_fecha DATE)
RETURNS DECIMAL(14,2)
READS SQL DATA
BEGIN
    DECLARE v_saldo DECIMAL(14,2);

    SELECT COALESCE(SUM(CASE tipo
                            WHEN 'AHORRO_ORDINARIO'  THEN monto
                            WHEN 'AHORRO_VOLUNTARIO' THEN monto
                            WHEN 'LIQUIDACION_CICLO' THEN -monto
                            ELSE 0
                        END), 0)
      INTO v_saldo
      FROM movimiento
     WHERE ahorrador_id = p_ahorrador_id
       AND fecha <= p_fecha;

    RETURN v_saldo;
END
;;
DELIMITER ;

-- Ahorro acumulado del ciclo en curso: solo cuentan las aportaciones posteriores a la última liquidación.
-- La fecha de la última liquidación se busca con una subconsulta por ahorrador (usa el índice
-- ahorrador_id, fecha) en lugar de una tabla derivada: así la vista se puede unir a la lista de
-- ahorradores sin recalcularse por cada renglón (ver docs/indices.md, C7).
CREATE VIEW v_saldo_ahorrador AS
SELECT a.id AS ahorrador_id,
       a.caja_id,
       CAST(COALESCE(SUM(CASE WHEN m.tipo = 'AHORRO_ORDINARIO'  THEN m.monto END), 0) AS DECIMAL(14,2)) AS ordinario,
       CAST(COALESCE(SUM(CASE WHEN m.tipo = 'AHORRO_VOLUNTARIO' THEN m.monto END), 0) AS DECIMAL(14,2)) AS voluntario,
       CAST(COALESCE(SUM(m.monto), 0) AS DECIMAL(14,2)) AS total
  FROM ahorrador a
  LEFT JOIN movimiento m
         ON m.ahorrador_id = a.id
        AND m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO')
        AND m.fecha > COALESCE((SELECT MAX(l.fecha)
                                  FROM movimiento l
                                 WHERE l.ahorrador_id = a.id AND l.tipo = 'LIQUIDACION_CICLO'), '1000-01-01')
 GROUP BY a.id, a.caja_id
;

-- Saldo y avance de cada préstamo a partir de sus movimientos y de la tabla de amortización.
CREATE VIEW v_prestamo_resumen AS
SELECT p.id AS prestamo_id,
       p.folio,
       p.caja_id,
       p.ahorrador_id,
       p.monto,
       p.plazo_quincenas,
       p.tasa_mensual,
       p.estado,
       CAST(COALESCE(mv.capital_pagado, 0) AS DECIMAL(14,2)) AS capital_pagado,
       CAST(COALESCE(mv.interes_pagado, 0) AS DECIMAL(14,2)) AS interes_pagado,
       CAST(CASE WHEN COALESCE(mv.desembolsos, 0) > 0
                 THEN p.monto - COALESCE(mv.capital_pagado, 0)
                 ELSE 0 END AS DECIMAL(14,2)) AS saldo_vigente,
       COALESCE(am.cuotas_aplicadas, 0) AS cuotas_aplicadas,
       p.plazo_quincenas - COALESCE(am.cuotas_aplicadas, 0) AS cuotas_restantes
  FROM prestamo p
  LEFT JOIN (SELECT prestamo_id,
                    SUM(CASE WHEN tipo = 'ABONO_CAPITAL' THEN monto END) AS capital_pagado,
                    SUM(CASE WHEN tipo = 'ABONO_INTERES' THEN monto END) AS interes_pagado,
                    SUM(tipo = 'DESEMBOLSO_PRESTAMO') AS desembolsos
               FROM movimiento
              WHERE prestamo_id IS NOT NULL
              GROUP BY prestamo_id) mv ON mv.prestamo_id = p.id
  LEFT JOIN (SELECT prestamo_id, COUNT(*) AS cuotas_aplicadas
               FROM prestamo_amortizacion
              WHERE estado = 'APLICADO'
              GROUP BY prestamo_id) am ON am.prestamo_id = p.id
;

-- Movimientos con su efecto sobre el ahorro y el saldo de ahorro acumulado renglón por renglón.
CREATE VIEW v_estado_cuenta AS
SELECT m.id AS movimiento_id,
       m.caja_id,
       m.ahorrador_id,
       m.periodo_id,
       m.prestamo_id,
       m.fecha,
       m.tipo,
       m.monto,
       m.referencia,
       CASE m.tipo
            WHEN 'AHORRO_ORDINARIO'  THEN m.monto
            WHEN 'AHORRO_VOLUNTARIO' THEN m.monto
            WHEN 'LIQUIDACION_CICLO' THEN -m.monto
            ELSE 0
       END AS efecto_ahorro,
       SUM(CASE m.tipo
                WHEN 'AHORRO_ORDINARIO'  THEN m.monto
                WHEN 'AHORRO_VOLUNTARIO' THEN m.monto
                WHEN 'LIQUIDACION_CICLO' THEN -m.monto
                ELSE 0
           END) OVER (PARTITION BY m.ahorrador_id ORDER BY m.fecha, m.id) AS saldo_ahorro
  FROM movimiento m
;

-- Descuento de nómina por ahorrador y periodo: ordinario + voluntario (si está activo) + cuota de préstamo.
-- Una licencia, incapacidad o baja en el periodo suspende todo el descuento; SUSPENDE_VOLUNTARIO solo el voluntario.
-- Conviene filtrar siempre por periodo_id.
CREATE VIEW v_descuento_periodo AS
SELECT t.ahorrador_id,
       t.caja_id,
       t.periodo_id,
       t.suspendido,
       IF(t.suspendido, 0, t.cuota_ordinaria) AS cuota_ordinaria,
       IF(t.suspendido OR t.suspende_voluntario OR NOT t.voluntario_activo, 0, t.cuota_voluntaria) AS cuota_voluntaria,
       IF(t.suspendido, 0, t.cuota_prestamo) AS cuota_prestamo,
       IF(t.suspendido, 0,
          t.cuota_ordinaria
          + IF(t.suspende_voluntario OR NOT t.voluntario_activo, 0, t.cuota_voluntaria)
          + t.cuota_prestamo) AS total
  FROM (
        SELECT a.id AS ahorrador_id,
               a.caja_id,
               p.id AS periodo_id,
               cu.cuota_ordinaria,
               cu.cuota_voluntaria,
               cu.voluntario_activo,
               EXISTS (SELECT 1 FROM incidencia i
                        WHERE i.ahorrador_id = a.id AND i.periodo_id = p.id
                          AND i.tipo IN ('LICENCIA', 'INCAPACIDAD', 'BAJA')) AS suspendido,
               EXISTS (SELECT 1 FROM incidencia i
                        WHERE i.ahorrador_id = a.id AND i.periodo_id = p.id
                          AND i.tipo = 'SUSPENDE_VOLUNTARIO') AS suspende_voluntario,
               COALESCE((SELECT SUM(am.total)
                           FROM prestamo_amortizacion am
                           JOIN prestamo pr ON pr.id = am.prestamo_id
                          WHERE pr.ahorrador_id = a.id
                            AND pr.estado IN ('VIGENTE', 'LIQUIDADO')
                            AND am.periodo_id = p.id), 0) AS cuota_prestamo
          FROM periodo p
          JOIN ahorrador a
            ON a.caja_id = p.caja_id
           AND a.fecha_ingreso <= p.fecha_fin
           AND (a.fecha_baja IS NULL OR a.fecha_baja > p.fecha_fin)
          JOIN cuota_ahorro cu
            ON cu.ahorrador_id = a.id
           AND cu.vigente_desde = (SELECT MAX(c2.vigente_desde)
                                     FROM cuota_ahorro c2
                                    WHERE c2.ahorrador_id = a.id
                                      AND c2.vigente_desde <= p.fecha_fin)
       ) t
;

DELIMITER ;;
-- Devenga el interés del periodo que contiene la fecha de corte: saldo insoluto al inicio del periodo
-- por la mitad de la tasa mensual (el mismo cálculo de la tabla de amortización). Lo invoca la tarea
-- programada; es idempotente (volver a correrlo para el mismo corte actualiza los renglones).
CREATE PROCEDURE sp_calcular_interes_devengado(IN p_caja_id INT, IN p_fecha_corte DATE)
BEGIN
    DECLARE v_periodo_id INT DEFAULT NULL;
    DECLARE v_inicio DATE;

    SELECT id, fecha_inicio INTO v_periodo_id, v_inicio
      FROM periodo
     WHERE caja_id = p_caja_id
       AND p_fecha_corte BETWEEN fecha_inicio AND fecha_fin
     LIMIT 1;

    IF v_periodo_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PERIODO_NO_ENCONTRADO';
    END IF;

    INSERT INTO interes_devengado (prestamo_id, periodo_id, fecha_corte, saldo_insoluto, interes)
    SELECT pr.id, v_periodo_id, p_fecha_corte, x.saldo,
           ROUND(x.saldo * pr.tasa_mensual / 100 / 2, 2)
      FROM prestamo pr
      JOIN (SELECT m.prestamo_id,
                   SUM(CASE
                           WHEN m.tipo = 'DESEMBOLSO_PRESTAMO' AND m.fecha < v_inicio THEN m.monto
                           WHEN m.tipo = 'ABONO_CAPITAL' AND m.fecha < v_inicio THEN -m.monto
                           ELSE 0
                       END) AS saldo
              FROM movimiento m
              JOIN prestamo p2 ON p2.id = m.prestamo_id
             WHERE p2.caja_id = p_caja_id
             GROUP BY m.prestamo_id
            HAVING saldo > 0) x ON x.prestamo_id = pr.id
    ON DUPLICATE KEY UPDATE periodo_id = VALUES(periodo_id),
                            saldo_insoluto = VALUES(saldo_insoluto),
                            interes = VALUES(interes);

    SELECT COUNT(*) AS prestamos_procesados, COALESCE(SUM(interes), 0) AS interes_total
      FROM interes_devengado d
      JOIN prestamo p3 ON p3.id = d.prestamo_id
     WHERE p3.caja_id = p_caja_id AND d.fecha_corte = p_fecha_corte;
END
;;
DELIMITER ;

DELIMITER ;;
-- Recalcula saldo-días, saldo promedio y días activos de cada ahorrador del ciclo (borrador recalculable).
-- Una aportación cuenta desde su fecha; una liquidación surte efecto al terminar su día. La asignación del
-- interés (método del mayor residuo) la hace la aplicación. Un ciclo cerrado no se puede recalcular.
CREATE PROCEDURE sp_saldo_dias_ciclo(IN p_ciclo_id INT)
BEGIN
    DECLARE v_caja_id INT DEFAULT NULL;
    DECLARE v_estado VARCHAR(20);
    DECLARE v_inicio DATE;
    DECLARE v_fin DATE;
    DECLARE v_total DECIMAL(18,2);

    SELECT caja_id, estado, MAKEDATE(anio, 1), STR_TO_DATE(CONCAT(anio, '-12-31'), '%Y-%m-%d')
      INTO v_caja_id, v_estado, v_inicio, v_fin
      FROM ciclo
     WHERE id = p_ciclo_id;

    IF v_caja_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'CICLO_NO_ENCONTRADO';
    END IF;
    IF v_estado = 'CERRADO' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'CICLO_CERRADO';
    END IF;

    DELETE FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;

    INSERT INTO ciclo_distribucion
        (ciclo_id, ahorrador_id, saldo_dias, saldo_promedio, dias_activos, participacion, interes_asignado, ahorro_liquidado)
    SELECT p_ciclo_id, x.ahorrador_id, x.saldo_dias,
           ROUND(x.saldo_dias / x.dias_activos, 2), x.dias_activos, 0, 0, x.ahorro_al_corte
      FROM (SELECT m.ahorrador_id,
                   SUM(CASE WHEN m.tipo = 'LIQUIDACION_CICLO'
                            THEN -m.monto * DATEDIFF(v_fin, m.fecha)
                            ELSE  m.monto * (DATEDIFF(v_fin, m.fecha) + 1) END) AS saldo_dias,
                   SUM(CASE WHEN m.tipo = 'LIQUIDACION_CICLO' THEN -m.monto ELSE m.monto END) AS ahorro_al_corte,
                   DATEDIFF(COALESCE(MAX(CASE WHEN m.tipo = 'LIQUIDACION_CICLO' THEN m.fecha END), v_fin),
                            MIN(CASE WHEN m.tipo <> 'LIQUIDACION_CICLO' THEN m.fecha END)) + 1 AS dias_activos
              FROM movimiento m
             WHERE m.caja_id = v_caja_id
               AND m.fecha BETWEEN v_inicio AND v_fin
               AND m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO', 'LIQUIDACION_CICLO')
             GROUP BY m.ahorrador_id
            HAVING MIN(CASE WHEN m.tipo <> 'LIQUIDACION_CICLO' THEN m.fecha END) IS NOT NULL) x;

    SELECT COALESCE(SUM(saldo_dias), 0) INTO v_total FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;
    IF v_total > 0 THEN
        UPDATE ciclo_distribucion
           SET participacion = ROUND(saldo_dias / v_total, 6)
         WHERE ciclo_id = p_ciclo_id;
    END IF;

    SELECT COUNT(*) AS ahorradores, v_total AS saldo_dias_total
      FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;
END
;;
DELIMITER ;
