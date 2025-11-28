# Sistema di Versioning Automatico

## ?? Panoramica

Il progetto OfficeVersionsCore utilizza un sistema di versioning automatico basato sulla data che genera numeri di versione nel formato:

```
Major.Minor.MMDD.Revision
```

**Esempio**: `1.0.1128.1`
- `1.0` = Versione Major.Minor
- `1128` = 28 Novembre (MMDD)
- `1` = Prima build del giorno

## ?? Come Funziona

Il versioning è gestito dal file `Directory.Build.props` che viene automaticamente importato da MSBuild in tutti i progetti.

### Componenti della Versione

| Componente | Descrizione | Esempio |
|------------|-------------|---------|
| **Major** | Versione principale, incremento manuale per breaking changes | `1` |
| **Minor** | Versione minore, incremento manuale per nuove feature | `0` |
| **Build** | Data corrente in formato MMDD (automatico) | `1128` |
| **Revision** | Contatore build del giorno (automatico in CI/CD) | `1` |

### InformationalVersion

Oltre alla versione standard, viene generata una `InformationalVersion` che include:
- Versione completa
- Hash del commit Git (quando disponibile)

**Formato**: `1.0.1128.1+a3f2b5c1`

## ?? Utilizzo

### Build Standard

```bash
# Build normale (usa revision = 1)
dotnet build

# Build con configuration Release
dotnet build -c Release
```

### Incrementare Manualmente la Revision

```bash
# Visualizzare la prossima revision
dotnet build /t:IncrementRevision

# Build con revision specifica
dotnet build /p:VersionRevision=2

# Build Release con revision specifica
dotnet build -c Release /p:VersionRevision=5
```

### Visualizzare la Versione Corrente

```bash
# Mostra solo il numero di versione
dotnet build /t:GetVersion

# Mostra informazioni dettagliate (incluse durante ogni build)
dotnet build
```

## ?? Integrazione CI/CD

### GitHub Actions

Il sistema rileva automaticamente `GITHUB_RUN_NUMBER` e `GITHUB_SHA`:

```yaml
- name: Build
  run: dotnet build -c Release
  # La revision sarà automaticamente il run number
  # Il commit hash sarà automaticamente incluso
```

### Azure DevOps

Il sistema rileva automaticamente `BUILD_BUILDNUMBER` e `BUILD_SOURCEVERSION`:

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Build'
  inputs:
    command: 'build'
    configuration: 'Release'
  # La revision viene estratta automaticamente da BUILD_BUILDNUMBER
```

## ?? File Generati

### version.txt

Durante ogni build viene creato un file `version.txt` nella directory di output con informazioni complete:

```
Version: 1.0.1128.1
InformationalVersion: 1.0.1128.1+a3f2b5c1
Build Date: 2024-11-28 18:30:45
Configuration: Release
Target Framework: net10.0
Source Revision: a3f2b5c1
```

**Location**: `bin/Debug/net10.0/version.txt` o `bin/Release/net10.0/version.txt`

## ?? Modificare le Versioni Major/Minor

Per aggiornare la versione principale, edita `Directory.Build.props`:

```xml
<PropertyGroup>
  <!-- Incrementa questi valori manualmente -->
  <VersionMajor>2</VersionMajor>
  <VersionMinor>0</VersionMinor>
  ...
</PropertyGroup>
```

### Quando Incrementare

- **Major**: Breaking changes, architettura completamente nuova
- **Minor**: Nuove feature, miglioramenti significativi
- **Build**: Automatico (data corrente)
- **Revision**: Automatico in CI/CD, manuale in locale se necessario

## ?? Esempi di Versioning

| Scenario | Comando | Versione Risultante |
|----------|---------|---------------------|
| Build locale del 28/11 | `dotnet build` | `1.0.1128.1` |
| Seconda build locale | `dotnet build /p:VersionRevision=2` | `1.0.1128.2` |
| Build CI/CD (run #42) | Automatico | `1.0.1128.42` |
| Nuova feature (29/11) | Incrementa Minor ? `dotnet build` | `1.1.1129.1` |
| Breaking change | Incrementa Major ? `dotnet build` | `2.0.1129.1` |

## ?? Output durante la Build

Durante ogni build vedrai un banner con le informazioni di versione:

```
??????????????????????????????????????????????????????????????
?  Building OfficeVersionsCore                               ?
??????????????????????????????????????????????????????????????
?  Version: 1.0.1128.1
?  Informational: 1.0.1128.1+a3f2b5c1
?  Configuration: Release
?  Build Date: 2024-11-28 18:30:45
??????????????????????????????????????????????????????????????
```

## ?? Note Tecniche

- Il file `Directory.Build.props` viene automaticamente importato da MSBuild
- Non è necessario modificare `.csproj` per utilizzare il versioning
- La generazione XML documentation è abilitata automaticamente
- Tutte le proprietà di versioning sono disponibili nei file assembly
- Il sistema funziona sia in locale che in pipeline CI/CD

## ?? Verificare la Versione nell'Assembly

Puoi verificare la versione compilata nell'assembly:

```csharp
// In C#
var version = Assembly.GetExecutingAssembly().GetName().Version;
var infoVersion = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion;

Console.WriteLine($"Version: {version}");
Console.WriteLine($"Info: {infoVersion}");
```

## ?? Best Practices

1. **Non committare modifiche manuali alla revision** - lascia che sia automatica
2. **Incrementa Major solo per breaking changes** significativi
3. **Incrementa Minor per nuove feature** o miglioramenti
4. **Documenta sempre** i cambiamenti di Major/Minor nel CHANGELOG
5. **Usa tag Git** per le release importanti: `git tag v1.0.1128.1`

## ?? Risorse

- [MSBuild Directory.Build.props](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory)
- [Assembly Versioning](https://learn.microsoft.com/en-us/dotnet/standard/assembly/versioning)
- [Semantic Versioning](https://semver.org/)
