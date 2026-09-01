# CLAUDE.md

# Central Translation Management Service

## 1. Purpose

This repository contains a Central Translation Management Service.

The service is the **single source of truth for translations** across the organisation.

It must support:

- Mobile applications
- Web applications
- Websites
- Backend microservices
- Other services that require translated content

The organisation may have multiple applications and services, currently approximately:

- 4 major applications/sites
- Multiple backend microservices
- Approximately 20 languages

The number of applications, services and languages must NOT be hard-coded.

The system must be designed so additional applications, services and languages can be added without changing the architecture.

---

# 2. Most Important Architectural Requirement

There are TWO ways the translation functionality will be consumed.

## Internal .NET Microservices

Internal .NET microservices that exist in the same solution should use the shared Translation Application/Library directly.

They should NOT make an HTTP request to the Translation API simply to obtain translations.

Example:

```text
Course Microservice
        │
        ▼
ITranslationService
        │
        ▼
Translation Application
        │
        ├── Redis
        │
        └── MongoDB
```

The internal microservice uses the translation functionality directly through dependency injection/shared code.

---

## External Applications and Websites

External applications and websites must consume translations through the Translation REST API.

Examples:

- .NET MAUI applications
- Websites
- React applications
- Angular applications
- External web applications
- Other externally deployed applications
- External services

Example:

```text
MAUI / Website
       │
       ▼
Translation REST API
       │
       ▼
Translation Application
       │
       ├── Redis
       │
       └── MongoDB
```

---

# 3. One Translation Engine

There must NOT be two separate translation implementations.

Both internal and external consumers must use the same underlying translation logic.

Architecture:

```text
                         Translation System
                                │
                         Translation Engine
                                │
                ┌───────────────┴───────────────┐
                │                               │
                ▼                               ▼
       Internal .NET Services             REST API
                │                               │
                ▼                               ▼
        Direct Application Call          External Clients
                │                               │
                ▼                               ▼
          Microservices                   MAUI / Web
```

The Translation API is an adapter around the same application layer used by internal services.

Do not duplicate translation resolution logic inside controllers.

---

# 4. MongoDB Ownership

MongoDB is the **single source of truth** for translations.

Only the Translation system should access the translation MongoDB collections.

Internal microservices must NOT directly query MongoDB translation collections.

Do NOT create:

```text
Course Microservice
      ↓
MongoDB Translation Collection
```

Instead:

```text
Course Microservice
      ↓
Translation Application
      ↓
MongoDB
```

External applications:

```text
Website
   ↓
Translation API
   ↓
Translation Application
   ↓
MongoDB
```

This ensures the Translation system owns:

- Translation storage
- Translation resolution
- Common translations
- Project translations
- Language fallback
- Publishing
- Translation status
- Translation history
- Cache management

---

# 5. Redis Ownership

Redis is a cache.

MongoDB remains the source of truth.

Only the Translation system should manage translation Redis entries.

Internal microservices should NOT independently manipulate translation cache keys.

External applications should NOT access Redis.

Architecture:

```text
                  Translation Application
                           │
                    ┌──────┴──────┐
                    │             │
                    ▼             ▼
                MongoDB         Redis
               Source of       Cache
                 Truth
```

---

# 6. Existing Solution Must Be Inspected First

This is an existing solution.

Before making changes, inspect the entire repository.

Do NOT immediately create a new implementation.

Understand:

- Existing solution structure
- Existing projects
- Existing microservices
- Existing APIs
- Existing translation implementation
- Existing MongoDB implementation
- Existing repositories
- Existing services
- Existing models
- Existing DTOs
- Existing authentication
- Existing authorisation
- Existing caching
- Existing Redis implementation
- Existing configuration
- Existing tests
- Existing Docker configuration
- Existing Azure infrastructure
- Existing Azure DevOps pipelines
- Existing NuGet packages

Look for:

- Duplicate functionality
- Dead code
- Unused classes
- Unused interfaces
- Unused projects
- Unused NuGet packages
- Obsolete translation models
- Duplicate translation storage
- Duplicate translation logic
- Unused configuration
- Unused endpoints
- Old database collections
- Old translation systems

Do not preserve code simply because it already exists.

---

# 7. Assessment Before Implementation

Before making major architectural changes, create:

```text
docs/existing-solution-assessment.md
```

Include:

```text
Existing Architecture
Existing Microservices
Existing Translation Architecture
Existing Database Architecture
Existing API Architecture
Existing Redis Architecture
Existing Authentication
Existing Pipelines
Existing Tests
Existing Dependencies
Unused Code Candidates
Duplicate Functionality
Obsolete Data
Recommended Changes
Recommended Removals
Migration Risks
```

The assessment should be based on the actual repository.

Do not guess.

---

# 8. Do Not Blindly Rebuild

This is a refactoring and enhancement task.

If existing code already performs part of the required functionality correctly:

- Reuse it
- Refactor it
- Move it
- Simplify it

Do NOT create a duplicate implementation.

For example, if an existing translation resolver already exists:

```text
Existing TranslationResolver
```

do not create:

```text
NewTranslationResolver
```

unless there is a clear technical reason.

---

# 9. Target Solution Structure

The final solution should follow a clean architecture similar to:

```text
src/
│
├── Translation.Api
│
├── Translation.Application
│
├── Translation.Domain
│
├── Translation.Infrastructure
│
├── Translation.Contracts
│
├── Translation.Admin
│
└── Translation.Client
│
tests/
│
├── Translation.UnitTests
├── Translation.IntegrationTests
└── Translation.ApiTests
│
infra/
│
├── docker
└── azure
│
pipelines/
│
└── templates
│
docs/
```

However, do not force this structure if the existing solution has a better established architecture.

The final dependency direction must remain clean.

---

# 10. Domain Layer

The Domain layer must contain translation business concepts and rules.

Potential entities:

```text
Application / Project
Language
TranslationKey
Translation
TranslationHistory
```

Do not create entities that are not required.

---

# 11. Application Layer

The Application layer contains translation business logic.

This is the most important reusable layer.

It must contain functionality such as:

```text
GetTranslations
ResolveTranslations
ResolveFallback
ResolveCommonTranslations
ResolveProjectTranslations
PublishTranslation
UpdateTranslation
CreateTranslation
ReviewTranslation
ApproveTranslation
```

The application layer must NOT depend on ASP.NET controllers.

It must NOT depend on HTTP.

It must NOT require a web request to operate.

This is what allows internal microservices to use it directly.

---

# 12. Shared Translation Service

Create a reusable abstraction such as:

```csharp
public interface ITranslationService
{
    Task<TranslationBundle> GetTranslationsAsync(
        string project,
        string language,
        CancellationToken cancellationToken = default);
}
```

The exact naming may differ depending on the existing architecture.

The important requirement is that internal .NET microservices can inject the service:

```csharp
public class CourseService
{
    private readonly ITranslationService _translationService;

    public CourseService(
        ITranslationService translationService)
    {
        _translationService = translationService;
    }
}
```

and call:

```csharp
var translations =
    await _translationService.GetTranslationsAsync(
        "nimbus",
        "fr-FR",
        cancellationToken);
```

There should be NO HTTP request involved.

---

# 13. Dependency Injection

Provide a reusable registration method for internal .NET consumers.

For example:

```csharp
services.AddTranslationServices(configuration);
```

The exact implementation should follow the existing solution conventions.

It should register:

- Translation application services
- Translation repositories
- MongoDB
- Redis
- Translation resolution
- Required infrastructure

Internal microservices should be able to reference the shared translation project/library and register it cleanly.

---

# 14. Internal Microservice Consumption

Internal .NET microservices must consume the translation functionality directly.

Example:

```text
Course Service
      │
      ▼
ITranslationService
      │
      ▼
Translation Application
      │
      ├── Translation Repository
      │
      ├── Redis Cache
      │
      └── MongoDB
```

Do NOT implement:

```text
Course Service
      │
      ▼
HTTP Client
      │
      ▼
Translation API
```

when the Course Service can directly use the shared application/library.

This avoids unnecessary:

- HTTP overhead
- Network dependencies
- Serialisation
- Deserialisation
- Service-to-service traffic
- Failure points

---

# 15. Important Microservice Boundary

The fact that the projects are in the same solution does NOT mean every project should access everything.

The Translation system owns the translation domain.

Internal microservices may use the Translation Application through its public abstraction.

They must not bypass the Translation Application and directly access:

```text
MongoDB
Translation collections
Redis
Translation repositories
Internal persistence models
```

Use the public application/service interface.

---

# 16. External API

The Translation API exposes the translation functionality to external consumers.

Primary endpoint:

```http
GET /api/translations/{project}/{language}
```

Example:

```http
GET /api/translations/nimbus/fr-FR
```

The API controller should:

1. Validate request
2. Call ITranslationService
3. Handle ETag/HTTP caching where appropriate
4. Return the translation bundle

The controller must not contain translation business logic.

---

# 17. External Consumers

External consumers include:

```text
MAUI Applications
Websites
Web Applications
JavaScript Applications
React
Angular
Other Services
External Microservices
```

They use:

```http
GET /api/translations/{project}/{language}
```

The technology of the consuming client must not matter.

The API contract must remain platform independent.

---

# 18. Translation Payload

The service must return Common + Project translations in ONE payload.

Example:

```json
{
  "project": "nimbus",
  "language": "fr-FR",
  "translations": {
    "common.save": "Enregistrer",
    "common.cancel": "Annuler",
    "common.delete": "Supprimer",
    "course.start": "Commencer le cours",
    "course.complete": "Cours terminé",
    "course.progress": "Progression"
  }
}
```

Consumers should NOT have to make separate requests for:

```text
Common
```

and:

```text
Project
```

The Translation Service performs the combination.

---

# 19. Common Translations

There must be a Common translation scope shared between projects.

Examples:

```text
common.save
common.cancel
common.delete
common.close
common.back
common.next
common.previous
common.loading
common.error
common.retry
common.yes
common.no
```

Common translations are reusable by multiple projects.

Do not duplicate Common translations into every project.

---

# 20. Project Translations

Each project/application may have project-specific translations.

Examples:

```text
nimbus
website
customerportal
adminportal
```

Example:

```text
nimbus:
    course.start
    course.complete
    course.progress

website:
    home.title
    home.subtitle

customerportal:
    account.subscription
    account.billing
```

---

# 21. Common + Project Resolution

For:

```text
project = nimbus
language = fr-FR
```

resolve:

```text
Common/fr-FR
+
Nimbus/fr-FR
```

into:

```text
TranslationBundle
```

The consuming client receives only the final resolved bundle.

---

# 22. Project Overrides

If the architecture supports project-specific overrides, the resolution priority should be:

```text
Project
   ↓
Common
   ↓
Language Fallback
```

Example:

Common:

```text
common.cancel = Cancel
```

Project:

```text
common.cancel = Exit Course
```

Result:

```text
common.cancel = Exit Course
```

Do not introduce this functionality unnecessarily if the existing requirements do not require it.

---

# 23. Languages

Languages must be data driven.

Do NOT hard-code 20 languages.

Examples:

```text
en-GB
fr-FR
de-DE
es-ES
it-IT
pt-PT
nl-NL
ar-AE
```

Additional languages must be addable without code changes.

---

# 24. Language Fallback

Support configurable language fallback.

Example:

```text
fr-CA
   ↓
fr-FR
   ↓
en-GB
```

When resolving a translation:

1. Try requested language
2. Try configured fallback
3. Continue fallback
4. Use default language

Do not return an empty translation when a valid fallback exists.

---

# 25. Translation Status

Translations must have a lifecycle:

```text
Draft
InReview
Approved
Published
Archived
```

Only:

```text
Published
```

translations are returned to consuming applications and services.

Internal microservices and external applications must not accidentally receive Draft or InReview translations.

---

# 26. Translation History

Keep an audit history of translation changes.

History should include:

```text
Translation key
Project
Language
Old value
New value
Action
Changed by
Changed at
```

History is for:

- Auditing
- Tracking changes
- Investigation
- Rollback support

History is NOT numeric translation versioning.

---

# 27. No Numeric Translation Versioning

Do NOT create:

```text
Version 1
Version 2
Version 3
```

for every translation.

If:

```text
course.start = Start Course
```

changes to:

```text
course.start = Begin Course
```

the key remains:

```text
course.start
```

Use:

```text
UpdatedAt
ContentHash
ETag
```

for change detection.

---

# 28. ETag

Translation bundles must support ETags.

Example:

```http
GET /api/translations/nimbus/fr-FR

ETag: "abc123"
```

The client can send:

```http
If-None-Match: "abc123"
```

If the bundle has not changed:

```http
304 Not Modified
```

If it has changed:

```http
200 OK
ETag: "xyz789"
```

The ETag must represent the complete resolved bundle.

It must change when:

- A Common translation changes
- A Project translation changes
- A fallback translation changes
- A published translation is added
- A published translation is removed

---

# 29. Redis Cache

Cache the final resolved translation bundle.

Recommended cache key:

```text
translations:{project}:{language}
```

Example:

```text
translations:nimbus:fr-FR
```

The cached value should already contain:

```text
Common
+
Project
+
Fallback resolution
```

Consumers should not perform this combination.

---

# 30. Cache Invalidation

When a Project translation changes:

```text
translations:nimbus:fr-FR
```

should be invalidated.

When a Common translation changes:

all affected project/language bundles must be invalidated.

For example:

```text
Common/fr-FR
```

may affect:

```text
nimbus/fr-FR
website/fr-FR
customerportal/fr-FR
adminportal/fr-FR
```

Do not unnecessarily invalidate unrelated languages.

---

# 31. MongoDB Data Model

Use MongoDB for persistence.

Expected concepts:

```text
Applications / Projects
Languages
TranslationKeys
Translations
TranslationHistory
```

Do not create unnecessary collections.

Recommended uniqueness:

```text
Project + Key
```

for translation keys.

And:

```text
Project + Language + TranslationKey
```

for translations.

Use appropriate MongoDB indexes.

---

# 32. MongoDB Is Not a Shared Integration Database

This is important.

Other microservices must NOT depend directly on translation MongoDB collections.

Do not implement:

```text
Course Service → MongoDB Translation collection
User Service → MongoDB Translation collection
Content Service → MongoDB Translation collection
```

Instead:

```text
Course Service
      ↓
Translation Application

User Service
      ↓
Translation Application

Content Service
      ↓
Translation Application
```

External applications:

```text
External App
      ↓
Translation API
```

This maintains ownership of the Translation domain.

---

# 33. Admin UI

There must be a central web-based Translation Management UI.

The UI must allow authorised users to:

- Manage projects
- Manage languages
- Create keys
- Edit translations
- Search translations
- Filter translations
- Identify missing translations
- Submit for review
- Approve translations
- Publish translations
- View translation history

The Admin UI must use management APIs/application services.

It must NOT directly access MongoDB.

---

# 34. Translation Grid

Provide a translation grid similar to:

```text
Key                    English          French
-----------------------------------------------------
common.save            Save             Enregistrer
common.cancel          Cancel           Annuler
course.start           Start Course     Commencer le cours
course.complete        Complete         Terminé
```

Allow filtering by:

```text
Project
Language
Category
Status
Common / Project
Missing
```

Support RTL languages such as Arabic.

---

# 35. Management API vs Consumer API

Separate management operations from translation consumption.

Management functionality:

```text
Projects
Languages
Keys
Translations
Review
Approval
Publishing
History
```

Consumer functionality:

```http
GET /api/translations/{project}/{language}
```

The consumer endpoint must be optimised for:

- Speed
- Caching
- Large translation bundles
- Low network usage

---

# 36. Do Not Create Per-Key APIs

Do NOT implement:

```text
GET /translation/common.save
GET /translation/common.cancel
GET /translation/course.start
```

Clients should retrieve a translation bundle.

Use:

```http
GET /api/translations/{project}/{language}
```

This reduces network traffic and simplifies caching.

---

# 37. Internal Service API

Internal microservices should generally use:

```csharp
ITranslationService
```

rather than:

```csharp
HttpClient
```

for translation retrieval.

The shared application service should return the same conceptual:

```text
TranslationBundle
```

used by the API.

---

# 38. External Client SDK

An SDK is NOT required for the core Translation Service.

The REST API must be sufficient for external consumers.

However, create a reusable client library if it is useful for the organisation's .NET applications.

For example:

```text
Company.Translation.Client
```

This may provide:

- HTTP communication
- ETag
- Local caching
- Offline support
- Fallback
- Translation lookup

The SDK must remain a client of the API.

It must NOT replace the central Translation Service.

---

# 39. .NET MAUI

MAUI applications should eventually be able to use a reusable client package.

The client should:

1. Load cached translations
2. Start application
3. Contact Translation API
4. Send If-None-Match
5. Handle 304
6. Handle 200
7. Save updated bundle
8. Work offline where appropriate

Example:

```csharp
var value =
    translationService.Get("course.start");
```

The MAUI client should NOT access MongoDB or Redis.

---

# 40. Websites

Websites should consume:

```http
GET /api/translations/{project}/{language}
```

They should cache the bundle appropriately.

The website should not know how translations are stored.

---

# 41. Other Microservices

Other microservices may need translations for:

- Error messages
- Notifications
- Emails
- System messages
- Validation messages
- Other user-facing content

They should use the shared Translation Application if they are internal .NET services.

Example:

```csharp
var bundle =
    await translationService.GetTranslationsAsync(
        "customerportal",
        "fr-FR",
        cancellationToken);
```

They must not create their own translation dictionaries.

---

# 42. Business Microservices Should Remain Language Independent

Business microservices should preferably return stable codes.

Example:

```json
{
  "code": "COURSE_NOT_FOUND"
}
```

rather than:

```json
{
  "message": "Course not found"
}
```

The consuming application can resolve:

```text
errors.course_not_found
```

through the Translation Service.

Do not put translation dictionaries into business microservices.

---

# 43. Remove Unused Code

Inspect the existing solution for:

- Unused projects
- Unused classes
- Unused interfaces
- Unused DTOs
- Unused repositories
- Unused services
- Unused NuGet packages
- Unused configuration
- Obsolete translation models
- Duplicate translation implementations
- Obsolete endpoints

Remove code only when it is confirmed to be unused or obsolete.

Do not delete uncertain functionality.

---

# 44. Remove Unwanted Data Structures

Identify old translation-related:

- Collections
- Models
- Tables
- Configuration
- Cached data
- Files

Do NOT automatically delete production data.

Instead:

1. Identify it.
2. Determine what depends on it.
3. Document it.
4. Create a migration/cleanup script.
5. Validate migration.
6. Only then remove obsolete data.

---

# 45. Authentication

Management functionality must be protected.

Use the existing authentication architecture if appropriate.

Otherwise support Microsoft Entra ID / OpenID Connect.

Suggested roles:

```text
TranslationAdministrator
TranslationManager
Translator
TranslationReviewer
TranslationReadOnly
```

Consumer access should be separate from management access.

External applications should not receive management permissions merely because they consume translations.

---

# 46. Authorisation

Example:

Administrator:

```text
Everything
```

Manager:

```text
Create
Edit
Review
Approve
Publish
```

Translator:

```text
Create
Edit
Submit for Review
```

Reviewer:

```text
Review
Approve
```

ReadOnly:

```text
View
```

Enforce permissions at the application/API layer.

---

# 47. Security

Do not commit:

- Passwords
- API keys
- Client secrets
- MongoDB credentials
- Redis credentials
- Certificates

Use:

- Environment variables
- Azure Key Vault
- Secure pipeline variables
- Managed identities where appropriate

---

# 48. Health Checks

Provide:

```text
/health
/health/live
/health/ready
```

Readiness should verify critical dependencies such as MongoDB.

Redis should be treated according to its role as a cache and should not make the service unusable simply because the cache is unavailable unless explicitly required by the architecture.

---

# 49. Logging

Use structured logging.

Important events include:

```text
Translation created
Translation updated
Translation submitted
Translation approved
Translation published
Translation cache hit
Translation cache miss
Translation cache invalidated
Translation bundle generated
```

Do not log sensitive information unnecessarily.

---

# 50. Failure Behaviour

If Redis is unavailable:

```text
Translation Application
        ↓
MongoDB
```

The service should continue to operate.

Redis is a performance optimisation, not the source of truth.

If MongoDB is unavailable, return an appropriate service error.

Do not allow external clients to receive incorrect translation data.

---

# 51. Testing

Test the shared Translation Application independently of HTTP.

Test:

```text
Common resolution
Project resolution
Common + Project combination
Fallback
Overrides
Publishing
History
```

API tests must test:

```text
GET bundle
ETag
If-None-Match
304
200
Validation
Authentication
Authorisation
```

Integration tests must test:

```text
MongoDB
Redis
Cache invalidation
```

Internal microservice integration must verify that the shared Translation Application can be registered and consumed without HTTP.

---

# 52. CI/CD

Preserve the existing Azure DevOps architecture if it is already appropriate.

Ensure pipelines perform:

```text
Restore
Build
Unit Tests
Integration Tests
Code Coverage
Security/Dependency Checks
Docker Build
Publish Artifacts
Deploy
```

Environments:

```text
Development
Test
Staging
Production
```

Production deployment must require approval.

Do not store secrets in YAML.

Use:

- Variable Groups
- Service Connections
- Azure Key Vault
- Managed Identity
- Secure variables

where appropriate.

---

# 53. Docker

If Docker is already used, integrate with the existing implementation.

If required, provide Docker support for:

```text
Translation API
Translation Admin
MongoDB
Redis
```

Local development should be possible through Docker Compose where appropriate.

Do not place production secrets in Docker Compose.

---

# 54. Documentation

Maintain:

```text
docs/
├── existing-solution-assessment.md
├── architecture.md
├── database.md
├── api.md
├── internal-consumption.md
├── external-consumption.md
├── authentication.md
├── authorisation.md
├── caching.md
├── etag.md
├── translation-workflow.md
├── local-development.md
├── docker.md
├── azure-deployment.md
├── azure-devops.md
├── maui-client.md
├── migration.md
└── troubleshooting.md
```

Document the difference between:

```text
Internal consumption
```

and:

```text
External consumption
```

clearly.

---

# 55. Final Architecture

The final architecture should conceptually look like:

```text
                              ┌─────────────────────┐
                              │      MongoDB        │
                              │                     │
                              │ Source of Truth     │
                              └──────────┬──────────┘
                                         │
                                         ▼
                              ┌─────────────────────┐
                              │ Translation         │
                              │ Application         │
                              │                     │
                              │ Common              │
                              │ Project             │
                              │ Fallback            │
                              │ Publishing           │
                              │ Resolution           │
                              └──────────┬──────────┘
                                         │
                              ┌──────────┴──────────┐
                              │                     │
                              ▼                     ▼
                         ┌─────────┐         ┌──────────────┐
                         │ Redis   │         │ Translation  │
                         │ Cache   │         │ REST API     │
                         └─────────┘         └───────┬──────┘
                                                     │
                                              External Consumers
                                                     │
                         ┌───────────────────────────┼────────────────────┐
                         │                           │                    │
                         ▼                           ▼                    ▼
                      MAUI Apps                  Websites          External Apps
```

Internal services use the Application directly:

```text
                 Translation Application
                          │
          ┌───────────────┼────────────────┐
          │               │                │
          ▼               ▼                ▼
      Course Service   User Service   Content Service
```

External applications use HTTP:

```text
                 Translation REST API
                          │
          ┌───────────────┼────────────────┐
          │               │                │
          ▼               ▼                ▼
        MAUI           Website       External Service
```

---

# 56. Final Principle

The system has ONE translation source and ONE translation engine.

```text
                         MongoDB
                       Source of Truth
                             │
                             ▼
                  Translation Application
                             │
                 ┌───────────┴───────────┐
                 │                       │
                 ▼                       ▼
        Internal .NET Services       REST API
                 │                       │
                 ▼                       ▼
          Microservices          External Consumers
```

Internal .NET microservices use the shared application directly.

External applications use the REST API.

Both paths produce the same translation result.

The Translation Service owns:

```text
Storage
Resolution
Common translations
Project translations
Fallback
Publishing
History
Caching
ETag
```

Consumers should only need to know:

```text
What project am I?
What language do I need?
```

Then request:

```http
GET /api/translations/{project}/{language}
```

and receive the complete published translation bundle.

---

# 57. Implementation Rules for Claude

When modifying this repository:

1. Inspect before modifying.
2. Understand the existing architecture.
3. Create the assessment document first.
4. Reuse good existing code.
5. Refactor instead of duplicating.
6. Remove confirmed unused code.
7. Do not remove uncertain functionality.
8. Do not introduce numeric translation versions.
9. Do not create per-key APIs.
10. Do not create separate Common and Project API calls for consumers.
11. Return Common + Project translations in one bundle.
12. Keep translation logic in the Application layer.
13. Keep MongoDB access inside Translation Infrastructure.
14. Do not allow other microservices to directly access translation MongoDB collections.
15. Do not allow external clients to access MongoDB.
16. Do not allow external clients to access Redis.
17. Internal .NET microservices should use ITranslationService directly.
18. External applications should use the REST API.
19. Both paths must use the same translation logic.
20. Keep languages configurable.
21. Keep projects configurable.
22. Only return Published translations.
23. Support fallback.
24. Support ETag.
25. Support If-None-Match.
26. Return 304 when appropriate.
27. Invalidate the correct Redis cache entries.
28. Do not hard-code secrets.
29. Do not duplicate translation dictionaries in business microservices.
30. Build after significant changes.
31. Run tests after significant changes.
32. Fix compilation errors before continuing.
33. Update documentation when architecture changes.
34. Do not make unnecessary architectural changes.
35. Do not over-engineer the solution.

---

# 58. Definition of Done

The implementation is complete when:

- One central translation system exists.
- MongoDB is the source of truth.
- Redis is a cache.
- Internal .NET microservices can directly use ITranslationService.
- Internal microservices do not require HTTP to obtain translations.
- Internal microservices do not directly access translation MongoDB collections.
- External applications can use the REST API.
- Websites can use the REST API.
- MAUI applications can use the REST API.
- Common translations exist.
- Project-specific translations exist.
- Common + Project are returned in one payload.
- Projects are configurable.
- Languages are configurable.
- Approximately 20 languages can be supported without code changes.
- Language fallback works.
- Only Published translations are returned.
- Translation history exists.
- No numeric translation versioning exists.
- ETag support exists.
- If-None-Match support exists.
- 304 responses work.
- Redis cache invalidation works.
- Common translation changes invalidate affected bundles.
- Project translation changes invalidate affected bundles.
- Admin UI exists.
- Translation workflow exists.
- Authentication exists.
- Authorisation exists.
- Unused code is removed where safe.
- Unused dependencies are removed.
- Obsolete translation structures are identified.
- Existing business functionality remains intact.
- Unit tests exist.
- Integration tests exist.
- API tests exist.
- Docker works.
- Azure DevOps pipelines build successfully.
- Production deployment is approval protected.
- Documentation is complete.

The implementation must be production-ready, maintainable, simple and understandable.