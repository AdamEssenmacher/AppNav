# Roadmap

AppNav is built in a planned order, so that each part lands on top of the parts it depends on. The [architecture](architecture.md) describes the design this order builds.

## How work lands

- Work lands in small pull requests. Each adds one discrete, named capability, with its tests and documentation, and `main` builds and passes its tests after every one.
- A milestone below is a grouping of related pull requests, not a pull request. Its pull requests are planned when the milestone starts, and the order within a milestone may shift.
- No package is published until the core release gate (milestone 12).

A checked box means the work is merged to `main`.

## Repository

- [x] Build skeleton: solution, central package management, warnings as errors, public API tracking, trim and AOT analyzers
- [x] Continuous integration: build and test on Linux, Windows and macOS; format check; pack
- [x] Contribution and security guidance, and issue and pull request templates
- [x] Dependabot updates for GitHub Actions and NuGet
- [x] Architecture overview and roadmap

## Core

The core is the `AdamE.AppNav` package. Its milestones are the layers in the [architecture](architecture.md#layers), in the same order.

- [ ] **1. Routing (in progress).** Route templates with literal, required, optional and catch-all parameters; constraints and value codecs; query strings; the route table, with match precedence that does not depend on registration order and conflicts reported when the table is built; formatting routes back to URIs; route metadata; convention routes bound from their template; route table modules.
- [ ] **2. Navigation state.** `NavigationState` and its window, stack, branch-host and modal nodes; route entries; structural identity and validation; `NavigationPlan`; metadata lifetimes.
- [ ] **3. Requests, provenance and deferral.** `RouterNavigationRequest` with its source, disposition, window, metadata and timestamp; request provenance; the deferred-request store contract and an in-memory store.
- [ ] **4. Planner contracts and policies.** The planner contract, request transformers, request policies, and the access gate with its access evaluator.
- [ ] **5. Navigation models.** The stack and branch-host models, route recipes, and the planner that applies them with each disposition, including contextual fallback.
- [ ] **6. Presenter contract and topology.** `INavigationPresenter`, presentation context, reconciliation events, and `NavigationTopology` for checking a plan against a presenter's supported shape.
- [ ] **7. Diagnostics.** Structured diagnostic events, safe and full data modes with redaction, logging, and activity sources for tracing.
- [ ] **8. The router.** `IRouterNavigator`: the navigation pipeline, results, fallback routes and history, operation serialization, redirect-loop protection, reconciliation and refresh.
- [ ] **9. Back navigation.** The back navigator contract, the default logical Back behavior, Back policies that can cancel a candidate plan, and the router's Back operation.
- [ ] **10. Deferred navigation.** The versioned request serializer and the replayer.
- [ ] **11. Route-entry leases.** Leases that own what a route entry borrows, the lease registry, their retirement and release as entries leave the committed state, and their integration into the router's commit.
- [ ] **12. Core release gate.** Allocation budgets, package inspection, a clean consumer build from the packed package, the release workflow, and the first preview package of `AdamE.AppNav`.

## Presenters

Presenter milestones follow the core. They are less settled than the core and may change before they start; each is planned in small pull requests the same way.

- [ ] **13. Presentation transactions.** A UI-independent presenter layer, shared by the native presenters below, that applies plans to a replaceable navigation surface and reconciles native gestures into navigation state.
- [ ] **14. Screen hosting.** Hosting that mounts each route's screen on the navigation surface, backed by its route-entry lease, shared by the native presenters below.
- [ ] **15. SwiftUI.** iOS scene hosting and SwiftUI rendering. Adds iOS builds to continuous integration.
- [ ] **16. Jetpack Compose.** Android activity hosting for a Navigation 3 surface. Adds Android builds to continuous integration.

## Not scheduled

These are under consideration, with no commitment and no place in the order. Each needs a decision before it is scheduled.

- A .NET MAUI presenter
- UIKit and Android view presenters for apps that use neither SwiftUI nor Compose
- A source generator for route registration
- Layouts that show more than one destination side by side
- Sample applications
