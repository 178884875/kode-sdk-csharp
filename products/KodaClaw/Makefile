# KodaClaw developer convenience Makefile

ROOT := $(CURDIR)
SOLUTION := $(ROOT)/KodaClaw.sln
WEB_DIR := $(ROOT)/apps/kodaclaw-web
DESKTOP_DIR := $(ROOT)/apps/kodaclaw-desktop
GATEWAY_PROJECT := $(ROOT)/src/KodaClaw.Gateway/KodaClaw.Gateway.csproj

GATEWAY_URL ?= http://127.0.0.1:5076
WORKSPACE_ROOT ?= $(HOME)/.kodaclaw

.PHONY: help \
	test-solution test-contract test-integration test-unit \
	web-build web-test web-e2e \
	desktop-test desktop-wave3-smoke desktop-managed-smoke desktop-package-smoke \
	verify-backend verify-web verify-desktop verify-all \
	run-gateway dev-web dev-desktop-attach dev-desktop-managed

help:
	@echo "KodaClaw Makefile targets:"
	@echo "  test-solution          Run dotnet test on KodaClaw.sln with -m:1"
	@echo "  test-contract          Run contract tests"
	@echo "  test-integration       Run integration tests"
	@echo "  test-unit              Run unit tests"
	@echo "  web-build              Build kodaclaw-web"
	@echo "  web-test               Run Vitest for kodaclaw-web"
	@echo "  web-e2e                Run Playwright E2E for kodaclaw-web"
	@echo "  desktop-test           Run desktop test suite"
	@echo "  desktop-wave3-smoke    Run desktop wave3 smoke"
	@echo "  desktop-managed-smoke  Run desktop managed-gateway smoke"
	@echo "  desktop-package-smoke  Run desktop package smoke"
	@echo "  verify-backend         Run solution regression"
	@echo "  verify-web             Run web build + unit + E2E"
	@echo "  verify-desktop         Run desktop test + smoke + package"
	@echo "  verify-all             Run backend + web + desktop verification"
	@echo "  run-gateway            Start local Gateway"
	@echo "  dev-web                Start Vite dev server"
	@echo "  dev-desktop-attach     Start desktop shell in AttachOnly mode"
	@echo "  dev-desktop-managed    Start desktop shell in ManagedChild mode"
	@echo ""
	@echo "Common overrides:"
	@echo "  GATEWAY_URL=$(GATEWAY_URL)"
	@echo "  WORKSPACE_ROOT=$(WORKSPACE_ROOT)"

test-solution:
	dotnet test "$(SOLUTION)" -m:1

test-contract:
	dotnet test "$(ROOT)/tests/KodaClaw.ContractTests/KodaClaw.ContractTests.csproj"

test-integration:
	dotnet test "$(ROOT)/tests/KodaClaw.IntegrationTests/KodaClaw.IntegrationTests.csproj"

test-unit:
	dotnet test "$(ROOT)/tests/KodaClaw.UnitTests/KodaClaw.UnitTests.csproj"

web-build:
	cd "$(WEB_DIR)" && npm run build

web-test:
	cd "$(WEB_DIR)" && npm run test

web-e2e:
	cd "$(WEB_DIR)" && npm run test:e2e

desktop-test:
	cd "$(DESKTOP_DIR)" && npm run test

desktop-wave3-smoke:
	cd "$(DESKTOP_DIR)" && npm run smoke:wave3

desktop-managed-smoke:
	cd "$(DESKTOP_DIR)" && npm run smoke:managed-gateway

desktop-package-smoke:
	cd "$(DESKTOP_DIR)" && npm run package:smoke

verify-backend: test-solution

verify-web:
	cd "$(WEB_DIR)" && npm run build && npm run test && npm run test:e2e

verify-desktop:
	cd "$(DESKTOP_DIR)" && npm run test && npm run smoke:wave3 && npm run smoke:managed-gateway && npm run package:smoke

verify-all: verify-backend verify-web verify-desktop

run-gateway:
	KODACLAW_WORKSPACE_ROOT="$(WORKSPACE_ROOT)" \
	KODACLAW_GATEWAY_URL="$(GATEWAY_URL)" \
	dotnet run --project "$(GATEWAY_PROJECT)" --urls "$(GATEWAY_URL)"

dev-web:
	cd "$(WEB_DIR)" && npm run dev

dev-desktop-attach:
	cd "$(DESKTOP_DIR)" && \
	KODACLAW_DESKTOP_GATEWAY_URL="$(GATEWAY_URL)" \
	KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE=AttachOnly \
	npm run dev

dev-desktop-managed:
	cd "$(DESKTOP_DIR)" && \
	KODACLAW_DESKTOP_GATEWAY_URL="$(GATEWAY_URL)" \
	KODACLAW_DESKTOP_GATEWAY_LIFECYCLE_MODE=ManagedChild \
	npm run dev
