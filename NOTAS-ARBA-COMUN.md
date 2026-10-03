# Notas para ARBA-comun (desde Acero-losas)

Cosas vistas al integrar `v1.0.0` que no se arreglan desde este repo (INTEGRACION.md §0): se anotan aqui y
el cambio, si procede, se hace en ARBA-comun con su version.

1. **Glob por defecto del SDK y el submodulo** (INTEGRACION.md §2). En un proyecto SDK (`Microsoft.NET.Sdk`) el
   `<Compile Include="**/*.cs" />` implicito ya arrastra `external/ARBA-comun/**/*.cs`: los de `src/` quedan
   duplicados con el `Include` del `.props` (error NETSDK1022) y ademas entran `tests/Program.cs` (otro `Main`)
   y `build/CheckUsage.cs`. Hace falta, **antes** del `Import`:

   ```xml
   <ItemGroup>
     <Compile Remove="external\**" />
     <None Remove="external\**" />
   </ItemGroup>
   <Import Project="external/ARBA-comun/Arba.Comun.props" />
   ```

   Convendria decirlo en INTEGRACION.md §2 (o que el `.props` ponga `external/ARBA-comun/**` en
   `DefaultItemExcludes`, aunque eso solo vale si el submodulo esta en la ruta por defecto).

2. **`ArbaMigration.MigrateHost` asegura los ocho parametros** del contrato (`EnsureAll`), no solo los tres que
   usa un add-in de armado. No es un problema (los GUID son fijos y el plugin de metrados los crea igual), pero
   conviene saberlo: tras "migrar" en un modelo aparecen tambien `ARBA - Anfitrion`, `Metrado - Partida`,
   `Metrado - Material`, `Metrado - Peso (kg)` y `Metrado - Pernos (und)`.
