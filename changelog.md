# Changelog

## [v1.0.0-beta](https://github.com/devlooped/CloudActors/tree/v1.0.0-beta) (2026-04-16)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v1.0.0-alpha...v1.0.0-beta)

:sparkles: Implemented enhancements:

- Enable STJ source-gen for actor state/event serialization [\#219](https://github.com/devlooped/CloudActors/pull/219) (@kzu)
- Rename and restructure CloudActors packages [\#215](https://github.com/devlooped/CloudActors/pull/215) (@kzu)
- Implement DCA005 diagnostic for non-serializable actor state types [\#214](https://github.com/devlooped/CloudActors/pull/214) (@kzu)

:twisted_rightwards_arrows: Merged:

- Avoid error during dotnet format from improper analyzer [\#218](https://github.com/devlooped/CloudActors/pull/218) (@kzu)
- Don't run Cecil/ILRepack in designtime builds [\#217](https://github.com/devlooped/CloudActors/pull/217) (@kzu)
- Update docs: clarify actor APIs, storage, and conventions [\#210](https://github.com/devlooped/CloudActors/pull/210) (@kzu)
- Fix improper hosting API call for cloud actors in SKILL.md [\#209](https://github.com/devlooped/CloudActors/pull/209) (@kzu)

## [v1.0.0-alpha](https://github.com/devlooped/CloudActors/tree/v1.0.0-alpha) (2026-04-15)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.5.0-rc.2...v1.0.0-alpha)

:sparkles: Implemented enhancements:

- Implement package-level skill consumable by agents [\#194](https://github.com/devlooped/CloudActors/pull/194) (@kzu)
- Make source generators properly incremental [\#193](https://github.com/devlooped/CloudActors/pull/193) (@kzu)
- Add comprehensive support for structured ids [\#191](https://github.com/devlooped/CloudActors/pull/191) (@kzu)

:bug: Fixed bugs:

- Fix  global namespace cases [\#205](https://github.com/devlooped/CloudActors/pull/205) (@kzu)

:hammer: Other:

- Add comprehensive AGENTS.md implementation reference [\#203](https://github.com/devlooped/CloudActors/issues/203)

:twisted_rightwards_arrows: Merged:

- Clarify automatic state persistence behavior [\#208](https://github.com/devlooped/CloudActors/pull/208) (@kzu)
- Update readme for Streamstone support [\#206](https://github.com/devlooped/CloudActors/pull/206) (@kzu)
- Add comprehensive AGENTS.md implementation reference [\#204](https://github.com/devlooped/CloudActors/pull/204) (@Copilot)
- Fall back to SolutionDir when git root is unavailable for skill copy [\#195](https://github.com/devlooped/CloudActors/pull/195) (@Copilot)

## [v0.5.0-rc.2](https://github.com/devlooped/CloudActors/tree/v0.5.0-rc.2) (2025-11-12)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.5.0-rc.1...v0.5.0-rc.2)

:sparkles: Implemented enhancements:

- Instrument the actor bus with standard-ish telemetry [\#162](https://github.com/devlooped/CloudActors/pull/162) (@kzu)

## [v0.5.0-rc.1](https://github.com/devlooped/CloudActors/tree/v0.5.0-rc.1) (2025-11-11)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.5.0-rc...v0.5.0-rc.1)

:sparkles: Implemented enhancements:

- Add callback after Raise happened in IEventSourced implementation [\#161](https://github.com/devlooped/CloudActors/pull/161) (@kzu)
- Allow relaxing snapshot version compatibility [\#160](https://github.com/devlooped/CloudActors/pull/160) (@kzu)
- Add AsDefault to Streamstone to follow Orleans conventions [\#157](https://github.com/devlooped/CloudActors/pull/157) (@kzu)

:twisted_rightwards_arrows: Merged:

- Don't expose analyzer and code fixers as public APIs [\#159](https://github.com/devlooped/CloudActors/pull/159) (@kzu)
- Add serializatoin metadata for event sourcing events too [\#158](https://github.com/devlooped/CloudActors/pull/158) (@kzu)

## [v0.5.0-rc](https://github.com/devlooped/CloudActors/tree/v0.5.0-rc) (2025-11-09)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.5.0-beta...v0.5.0-rc)

:sparkles: Implemented enhancements:

- Switch AddCloudActors namespace to M.E.DI [\#151](https://github.com/devlooped/CloudActors/pull/151) (@kzu)
- Proper diagnostics when ProduceReferenceAssembly=true, unsupported [\#148](https://github.com/devlooped/CloudActors/pull/148) (@kzu)

:bug: Fixed bugs:

- Do not forcedly pack the Orleans code generator [\#156](https://github.com/devlooped/CloudActors/pull/156) (@kzu)
- Remove unsupported VB from generators [\#155](https://github.com/devlooped/CloudActors/pull/155) (@kzu)
- Make sure we don't duplicate Orleans-generated code [\#154](https://github.com/devlooped/CloudActors/pull/154) (@kzu)

:twisted_rightwards_arrows: Merged:

- Minor xml doc clarification [\#152](https://github.com/devlooped/CloudActors/pull/152) (@kzu)
- Add end to end ASP.NET app that exposes actor over HTTP [\#149](https://github.com/devlooped/CloudActors/pull/149) (@kzu)

## [v0.5.0-beta](https://github.com/devlooped/CloudActors/tree/v0.5.0-beta) (2025-11-06)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.4.0...v0.5.0-beta)

:sparkles: Implemented enhancements:

- Rename interfaces project to Abstractions, Server to CloudActors [\#141](https://github.com/devlooped/CloudActors/pull/141) (@kzu)
- Allow reading actor state from storage snapshot [\#140](https://github.com/devlooped/CloudActors/pull/140) (@kzu)
- Add support for IVT builds without warnings, modernize codegen [\#135](https://github.com/devlooped/CloudActors/pull/135) (@kzu)
- Split interfaces from server/hosting package [\#88](https://github.com/devlooped/CloudActors/pull/88) (@kzu)
- Leverage Orleans codegen for increased compatiblity [\#70](https://github.com/devlooped/CloudActors/pull/70) (@kzu)
- Add code fixer for missing \[GenerateSerializer\] diagnostics [\#66](https://github.com/devlooped/CloudActors/pull/66) (@kzu)

:hammer: Other:

- Separate package for interfaces? [\#74](https://github.com/devlooped/CloudActors/issues/74)

:twisted_rightwards_arrows: Merged:

- Remove code fixer for \[GenerateSerializer\] which is unused [\#144](https://github.com/devlooped/CloudActors/pull/144) (@kzu)
- Adopt OSMF expand and improve the readme documentation [\#143](https://github.com/devlooped/CloudActors/pull/143) (@kzu)
- Improve Actor attribute allowing provider but no state name [\#137](https://github.com/devlooped/CloudActors/pull/137) (@kzu)
- Usings and XML doc cleanup/fixes [\#133](https://github.com/devlooped/CloudActors/pull/133) (@kzu)
- Convert to modern SLNX format [\#128](https://github.com/devlooped/CloudActors/pull/128) (@kzu)

## [v0.4.0](https://github.com/devlooped/CloudActors/tree/v0.4.0) (2024-06-14)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.3.0...v0.4.0)

:sparkles: Implemented enhancements:

- Update to streamstone v3 with latest Azure Data APIs [\#57](https://github.com/devlooped/CloudActors/pull/57) (@kzu)
- Simplify silo/services configuration and streamstone config [\#45](https://github.com/devlooped/CloudActors/pull/45) (@kzu)

:twisted_rightwards_arrows: Merged:

- Build full OS matrix, fix publish workflow [\#58](https://github.com/devlooped/CloudActors/pull/58) (@kzu)

## [v0.3.0](https://github.com/devlooped/CloudActors/tree/v0.3.0) (2023-08-08)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.2.2...v0.3.0)

## [v0.2.2](https://github.com/devlooped/CloudActors/tree/v0.2.2) (2023-08-07)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.2.1...v0.2.2)

## [v0.2.1](https://github.com/devlooped/CloudActors/tree/v0.2.1) (2023-08-07)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/v0.2.0...v0.2.1)

## [v0.2.0](https://github.com/devlooped/CloudActors/tree/v0.2.0) (2023-08-07)

[Full Changelog](https://github.com/devlooped/CloudActors/compare/607a992d701fcc6f70f130f704c2c520874b25e0...v0.2.0)



\* *This Changelog was automatically generated by [github_changelog_generator](https://github.com/github-changelog-generator/github-changelog-generator)*
