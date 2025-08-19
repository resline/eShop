# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

eShop is a reference .NET 9 application implementing an e-commerce website using a microservices architecture with .NET Aspire. The application demonstrates modern cloud-native patterns including containerization, event-driven communication, and distributed systems design.

## Architecture

### Core Technologies
- **.NET Aspire**: Orchestrates the distributed application components
- **PostgreSQL with pgvector**: Primary database (separate databases per service)
- **Redis**: Distributed caching for Basket service
- **RabbitMQ**: Message broker for event bus implementation
- **Identity Server**: Authentication and authorization (OpenID Connect)
- **YARP**: Reverse proxy for mobile BFF (Backend for Frontend)

### Service Architecture

The application follows Domain-Driven Design (DDD) and CQRS patterns with the following key services:

1. **eShop.AppHost** (`src/eShop.AppHost/`): .NET Aspire orchestrator that configures and runs all services
2. **Identity.API**: Authentication/authorization service using Identity Server
3. **Catalog.API**: Product catalog management with AI capabilities (optional OpenAI/Ollama integration)
4. **Basket.API**: Shopping cart service using Redis for session storage
5. **Ordering.API**: Order processing with DDD patterns (Domain, Infrastructure layers)
6. **OrderProcessor**: Background service for order workflow processing
7. **PaymentProcessor**: Payment processing simulation service
8. **Webhooks.API**: Webhook management and notification service
9. **WebApp**: Main Blazor web application frontend
10. **WebhookClient**: Admin client for webhook management

### Event-Driven Communication

Services communicate via:
- **Direct HTTP/gRPC calls** for synchronous operations
- **RabbitMQ event bus** for asynchronous integration events
- Integration events follow the pattern: `{Action}{Entity}IntegrationEvent` (e.g., `OrderStartedIntegrationEvent`)

## Development Commands

### Prerequisites
Ensure Docker Desktop is running before starting the application.

### Running the Application

```bash
# Run the entire application with .NET Aspire
dotnet run --project src/eShop.AppHost/eShop.AppHost.csproj

# The Aspire dashboard URL will be displayed in console output
# Look for: "Login to the dashboard at: http://localhost:19888/login?t=..."
```

### Building

```bash
# Build entire solution
dotnet build

# Build specific project
dotnet build src/Catalog.API/Catalog.API.csproj
```

### Testing

```bash
# Run all unit tests
dotnet test tests/Basket.UnitTests/Basket.UnitTests.csproj
dotnet test tests/Ordering.UnitTests/Ordering.UnitTests.csproj
dotnet test tests/ClientApp.UnitTests/ClientApp.UnitTests.csproj

# Run functional tests (requires services running)
dotnet test tests/Catalog.FunctionalTests/Catalog.FunctionalTests.csproj
dotnet test tests/Ordering.FunctionalTests/Ordering.FunctionalTests.csproj

# Run Playwright E2E tests
npm install
npx playwright test

# Run specific E2E test
npx playwright test e2e/AddItemTest.spec.ts
```

### Database Migrations

Each service with a database uses Entity Framework Core migrations:

```bash
# Catalog service
dotnet ef migrations add MigrationName -c CatalogContext -p src/Catalog.API -o Infrastructure/Migrations

# Ordering service  
dotnet ef migrations add MigrationName -c OrderingContext -p src/Ordering.Infrastructure -s src/Ordering.API -o Migrations

# Identity service
dotnet ef migrations add MigrationName -c ApplicationDbContext -p src/Identity.API -o Data/Migrations

# Webhooks service
dotnet ef migrations add MigrationName -c WebhooksContext -p src/Webhooks.API -o Migrations
```

## Key Implementation Patterns

### Domain-Driven Design (Ordering Service)
- **Aggregates**: Order, Buyer aggregates in `Ordering.Domain/AggregatesModel/`
- **Domain Events**: Raised within aggregates, handled by domain event handlers
- **Value Objects**: Address, CardType implementations
- **Repository Pattern**: Interfaces in Domain, implementations in Infrastructure

### CQRS Pattern
- Commands and queries separated in `Ordering.API/Application/`
- MediatR for command/query handling
- Integration with domain events

### Integration Events
- Located in `IntegrationEvents/Events/` folders
- Handlers in `IntegrationEvents/EventHandling/`
- Published via `IEventBus` after domain operations complete
- Transactional outbox pattern for reliability

### API Patterns
- Minimal APIs in newer services (Catalog, Ordering)
- OpenAPI/Swagger documentation auto-generated
- Health checks at `/health` endpoints
- Problem Details for error responses

## Configuration

### Environment Variables
- Connection strings configured in `appsettings.json`
- Override with `appsettings.Development.json` for local development
- Aspire passes configuration via environment variables to services

### Optional AI Features
- Set `useOpenAI = true` in `src/eShop.AppHost/Program.cs`
- Configure Azure OpenAI connection in `appsettings.json`:
  ```json
  "ConnectionStrings": {
    "OpenAi": "Endpoint=xxx;Key=xxx;"
  }
  ```

### Azure Deployment
Use Azure Developer CLI:
```bash
azd auth login
azd init
azd up
```

## Testing Approach

- **Unit Tests**: Test domain logic, services in isolation
- **Functional Tests**: Test API endpoints with test fixtures
- **E2E Tests**: Playwright tests for user scenarios
- **Test Data**: Sample catalog in `src/Catalog.API/Setup/catalog.json`