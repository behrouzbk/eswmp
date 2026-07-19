**Azure Architecture Reference**

Platform & Gateway Considerations for Future Projects

*A checklist of the Azure building blocks a service-gateway platform
needs*

**Purpose**

This document is a **forward-looking reference**, not a record of any
one deployment. It captures the Azure components and configuration
decisions that a gateway-style platform --- an API edge in front of a
set of containerised backend services --- should account for. Use it as
a checklist when standing up a similar project so that security,
observability, and operational concerns are considered up front rather
than discovered later.

> **How to read this:** each section names an Azure capability, what it
> does for the platform, and the decisions worth making deliberately.
> The specific SKUs, names, and sizes will differ per project --- what
> carries over is the set of things to decide.

**1. The overall shape**

A gateway platform on Azure typically has four layers, each mapping to a
group of Azure services:

- **The edge** --- a managed API gateway that authenticates,
  rate-limits, and routes inbound traffic (Azure API Management).

- **The compute** --- a container platform running the gateway and
  backend services (Azure Kubernetes Service).

- **The supporting services** --- secrets, identity, container images,
  and configuration (Key Vault, Entra ID, Container Registry).

- **The observability plane** --- logs, metrics, traces, alerts, and
  dashboards (Log Analytics, Application Insights, Azure Monitor).

The sections below walk each in turn, ending with the cross-cutting
concerns --- cost, environments, and the operational disciplines that
are easy to skip and expensive to retrofit.

**2. The edge --- API Management**

API Management (APIM) is the front door. Everything a partner or client
calls goes through it before reaching any service. Decisions to make:

**2.1 Tier selection**

APIM tiers differ significantly in capability and cost. The
**Consumption** tier is serverless and inexpensive but limited (no VNet
integration, reduced diagnostic categories, per-call billing). The
**Developer** tier is for non-production. **Standard / Premium** add
scale, VNet integration, multi-region, and full diagnostics. Choose
based on whether you need private networking, guaranteed throughput, and
per-request logging --- these are tier-gated and hard to change later.

> **Learned the hard way:** some diagnostic and logging features are not
> available on lower tiers. If per-request edge logging (who called
> what, with which status and latency) matters for supporting partners,
> confirm the tier supports it before committing.

**2.2 Policies to configure**

- **Authentication** --- validate inbound tokens (JWT validation against
  your identity provider) and require a subscription key. Two
  independent gates is a sound default.

- **Rate limiting / quotas** --- protect the backend and enforce
  per-consumer budgets.

- **Header handling** --- decide explicitly which headers pass through
  to the backend and which are stripped. Do not forward sensitive
  headers to every service by default.

- **Diagnostics** --- wire the gateway\'s request logs and metrics to
  your log workspace (this often requires a logger plus a diagnostic
  entity, not just a single setting).

**3. The compute --- Kubernetes (AKS)**

AKS runs the gateway and backend services as containers. Key decisions:

**3.1 Cluster setup**

- **Kubernetes version & upgrade channel** --- pick a supported version
  and decide how upgrades happen.

- **Node pools** --- size and count for the workload; consider a
  separate system pool. Right-size early; oversizing is a silent cost
  drain.

- **Networking** --- network plugin and whether the cluster is public or
  integrated into a VNet. This is foundational and painful to change
  later.

- **RBAC & identity integration** --- enable RBAC and integrate cluster
  access with your directory so access is managed centrally, not with
  static credentials.

> **Access gotcha:** if the cluster is not integrated with your
> directory (AAD/Entra), directory-based role assignments granting
> cluster access may silently have no effect. Confirm the cluster\'s
> access model matches how you intend to grant access, or roles you
> assign will appear granted but do nothing.

**3.2 Secrets into pods**

Backend services need connection strings and signing secrets. The clean
pattern is to keep them in Key Vault and mount them into pods via the
**Secrets Store CSI driver**, rather than baking them into images or
committing them to manifests.

> **Enable secret rotation from day one:** without rotation enabled, the
> CSI driver will not add a NEW key to an existing mounted secret ---
> forcing a disruptive delete-and-recreate every time you add a secret.
> Turning on rotation up front avoids a recurring operational tax. It is
> a no-downtime cluster setting.

**4. Supporting services**

**4.1 Key Vault**

- Store all secrets, connection strings, and signing keys here --- never
  in code, manifests, or images.

- Use distinct secrets for distinct trust boundaries (e.g. do not reuse
  an edge secret for internal signing).

- Plan rotation and access policies before go-live.

**4.2 Identity --- Entra ID**

- Register the applications (the gateway, and any consumer/partner apps)
  and define the token audiences and scopes.

- Prefer the client-credentials grant for service-to-service partner
  access.

- Use managed identities for Azure-to-Azure auth (e.g. AKS pulling from
  Key Vault) rather than stored credentials.

**4.3 Container Registry**

- Host your service images in a private registry the cluster can pull
  from via managed identity.

- Ensure images are built and tagged through your CI pipeline --- not
  built and pushed manually from a workstation.

> **Discipline worth enforcing:** images built outside the pipeline
> (locally, then pushed) drift from what the pipeline produces and are
> hard to trace. Make the pipeline the only path to a deployable image.

**5. The observability plane**

This is the layer most often under-built, and the one that determines
whether you can operate the platform. Treat it as a first-class
requirement, not an afterthought. It has four parts.

**5.1 Logs --- Log Analytics**

- Ship container logs to a Log Analytics workspace.

- Emit **structured (JSON) logs** so fields --- correlation id, status,
  operation --- become queryable columns. Plain-text logs with the
  correlation id on a separate line are far less useful, because log
  collectors ship each line as its own record.

- Set log retention deliberately, and avoid collecting sensitive values
  (e.g. exclude environment variables that contain connection strings).

**5.2 Traces --- Application Insights**

- Wire distributed tracing (OpenTelemetry) to Application Insights so
  you can follow one request across services as a connected waterfall.

- Confirm the exporter points at a live destination --- instrumentation
  that exports to a dead endpoint looks healthy but produces nothing.

- Link App Insights to the same Log Analytics workspace so traces and
  logs are queryable together.

**5.3 Alerts --- Azure Monitor**

- Define an **action group with a real receiver** (a distribution list
  or a Teams channel is better than a single person) and confirm a test
  alert actually arrives.

- Write alert rules for the failures that matter: upstream/backend
  faults, internal errors, and edge auth/rate-limit spikes. Decide which
  page a human and which are a daily digest.

- Set thresholds against real baselines --- too sensitive and people
  mute the alerts; too loose and real outages stay quiet.

> **The test that matters:** an alert is not \'done\' until a real
> message reaches a real inbox. \'The rule exists\' and \'Azure
> dispatched it\' are not the same as \'a human was told.\' Verify the
> whole chain end to end once.

**5.4 Dashboards**

- Build a single glance-able dashboard --- request volume, error rate,
  backend faults, edge auth failures, slowest operations.

- Define it as infrastructure-as-code (Bicep) in your repo so it is
  version-controlled and reproducible, not click-configured and lost.

**6. Cross-cutting considerations**

**6.1 Everything as infrastructure-as-code**

Define resources, alert rules, and dashboards in Bicep/ARM in the
repository. Config that lives only in the portal is invisible,
unreviewable, and lost when the environment is rebuilt. If it matters,
it belongs in version control.

**6.2 Environments**

Plan dev / staging / production as separate resource groups or
subscriptions from the start, with the same IaC parameterised per
environment. Decide early how a change flows from dev to production, and
make deployment go through a pipeline rather than manual steps.

**6.3 Cost**

- The largest costs are usually AKS node pools, APIM tier, and log/trace
  ingestion.

- Trace and log volume can grow quickly --- set sampling sensibly (100%
  in dev is fine; dial down for high-traffic production) and set
  retention deliberately.

- Right-size node pools; idle over-provisioned nodes are a steady,
  invisible drain.

**6.4 Security posture**

- No secrets in code, manifests, or images --- Key Vault only.

- Strip sensitive headers at the edge; forward to backends only what
  each needs.

- Sign and verify any identity context passed between the gateway and
  backends so it cannot be forged, and fail closed if the signing secret
  is absent.

- Enable secret rotation, RBAC, and directory-integrated access from the
  outset.

**7. Quick checklist**

A condensed version to run through when starting a similar platform:

  -------------------------------------------------------------------
  **Area**            **Decide / confirm**
  ------------------- -----------------------------------------------
  **API Management**  Tier (does it support the
                      diagnostics/networking you need?); auth
                      policies; rate limits; header allow-list;
                      diagnostics wired to logs

  **AKS**             K8s version & upgrades; node pool sizing;
                      networking (public vs VNet); RBAC +
                      directory-integrated access; CSI secret mount
                      with rotation ENABLED

  **Key Vault**       All secrets here; distinct secrets per trust
                      boundary; rotation & access policies

  **Identity**        App registrations, audiences, scopes;
                      client-credentials for partners; managed
                      identities for Azure-to-Azure

  **Container         Private registry; images built via pipeline
  Registry**          only

  **Log Analytics**   Structured JSON logs; correlation id in-row;
                      retention set; no secret capture

  **App Insights**    Distributed tracing to a LIVE endpoint; linked
                      to the log workspace

  **Alerts**          Action group with a real, verified receiver;
                      page-vs-digest rules; thresholds on real
                      baselines

  **Dashboard**       One glance-able board, defined as IaC in the
                      repo

  **Cross-cutting**   Everything as IaC; separate environments;
                      cost/sampling/retention set; security posture
                      confirmed
  -------------------------------------------------------------------

*--- End of reference ---*
