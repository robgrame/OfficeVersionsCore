# Windows Server Servicing Channels Documentation

## Overview

Windows Server supports multiple release and servicing channels to meet different organizational needs:

### 1. **Long-Term Servicing Channel (LTSC)**
- **Release Schedule**: Major releases every 2-3 years
- **Support Duration**: 5 years mainstream support + 5 years extended support (10 years total)
- **Target Audience**: Organizations requiring stability and predictability
- **Updates**: Security updates and critical fixes only
- **Examples**: 
  - Windows Server 2025 (LTSC)
  - Windows Server 2022 (LTSC)
  - Windows Server 2019 (LTSC)
  - Windows Server 2016 (LTSC)

### 2. **Annual Channel (AC)**
- **Release Schedule**: New version released annually (typically in September/October)
- **Support Duration**: 6 months mainstream support
- **Target Audience**: Organizations wanting latest features and improvements
- **Updates**: Feature updates, security updates, bug fixes
- **Naming Pattern**: Year + H1/H2 (e.g., 23H2, 24H2)
- **Examples**:
  - Windows Server 25H1 (hypothetical next Annual Channel)
  - Windows Server 24H2 (Annual Channel 2024 - 2nd release)
  - Windows Server 23H2 (Annual Channel 2023 - 2nd release)
  - Windows Server 23H1 (Annual Channel 2023 - 1st release)

## Historical Context

### Retired Channels

**Windows Server Semi-Annual Channel (SAC)** ❌ **RETIRED August 9, 2022**
- Used from Windows Server version 1709 to version 21H2
- Provided updates every 6 months
- Was replaced by the Annual Channel for more frequent releases

## Support Lifecycle Comparison

| Aspect | LTSC | Annual Channel |
|--------|------|----------------|
| **New Versions** | Every 2-3 years | Annually |
| **Mainstream Support** | 5 years | 6 months |
| **Extended Support** | 5 years | None |
| **Total Support** | 10 years | 6 months |
| **Update Frequency** | As needed | Monthly (Patch Tuesday) |
| **Feature Updates** | Major releases only | Included in each release |
| **Stability Focus** | High priority | Balance with features |

## Version Timeline

### Windows Server 2025
- **LTSC**: Windows Server 2025 (October 2024)
- **Annual Channel**: Windows Server 25H1, 25H2, etc.

### Windows Server 2022
- **LTSC**: Windows Server 2022 (October 2021)
- **Annual Channel**: Windows Server 23H1 (September 2023), 23H2 (September 2023)
- **Annual Channel**: Windows Server 24H1, 24H2 (2024 releases)

### Windows Server 2019
- **LTSC**: Windows Server 2019 (October 2018)
- **Support Ending**: January 9, 2024 (LTSC mainstream), January 14, 2026 (extended)

### Windows Server 2016
- **LTSC**: Windows Server 2016 (October 2016)
- **Support Ending**: January 11, 2022 (LTSC mainstream), January 12, 2027 (extended)

### Windows Server 2012 R2
- **LTSC**: Windows Server 2012 R2 (October 2013)
- **Support Ended**: October 9, 2018 (mainstream), October 13, 2023 (extended)

## Decision Matrix: Which Channel to Use?

### Choose **LTSC** if you:
- ✅ Prioritize stability and predictability
- ✅ Want longer support lifecycle (10 years)
- ✅ Need to minimize testing/update cycles
- ✅ Run mission-critical workloads
- ✅ Have infrequent infrastructure refresh cycles

### Choose **Annual Channel** if you:
- ✅ Want the latest features and improvements
- ✅ Can update every 6 months
- ✅ Benefit from rapid feature adoption
- ✅ Run containerized/cloud-native workloads
- ✅ Have DevOps-style rapid release processes

## Update Patterns

### LTSC Updates (Example: Windows Server 2022)
```
Windows Server 2022 (RTM)
  ↓ (KB articles, cumulative updates)
Windows Server 2022 (with monthly updates)
  ↓ (after 5 years)
Windows Server 2022 (Extended Support only)
```

### Annual Channel Updates (Example: Windows Server 23H2)
```
Windows Server 23H2 (Release)
  ↓ (monthly cumulative updates)
Windows Server 23H2 (Latest version)
  ↓ (after 6 months)
Windows Server 24H1 (Next Annual Channel)
```

## API and UI Filtering

The Windows Server Releases page provides filtering by:
- **Version**: 2025, 2022, 2019, 2016, 2012 R2
- **Servicing Channel**: LTSC, Annual Channel (AC)

This allows organizations to view releases and updates specific to their chosen servicing strategy.

## References

- [Windows Server Release Information](https://learn.microsoft.com/en-us/windows-server/get-started/windows-server-release-info)
- [Windows Server 2022 Support Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-server-2022)
- [Windows Server 2025 Support Lifecycle](https://learn.microsoft.com/en-us/lifecycle/products/windows-server-2025)
