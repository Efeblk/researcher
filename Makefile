HOST ?= http://localhost:5000
DATABASE_FILE := $(CURDIR)/academic.db

.PHONY: help run build clean health collect random

help:
	@echo "Kullanılabilir komutlar:"
	@echo "  make run                  HTTP sunucusunu başlatır"
	@echo "  make build                Projeyi derler"
	@echo "  make clean                Yerel SQLite veritabanını siler"
	@echo "  make health               Sunucunun çalıştığını kontrol eder"
	@echo "  make collect ID=...       Bir akademisyen kimliğini sorgular"
	@echo "  make random               Rastgele akademisyen özeti getirir"
	@echo "  make health HOST=...      Farklı bir sunucu adresi kullanır"

run:
	dotnet run

build:
	dotnet build

clean:
	@if [ -f "$(DATABASE_FILE)" ] && lsof "$(DATABASE_FILE)" >/dev/null 2>&1; then \
		echo "Veritabanı kullanımda. Önce çalışan sunucuyu durdur."; \
		exit 1; \
	fi
	rm -f -- \
		"$(DATABASE_FILE)" \
		"$(DATABASE_FILE)-shm" \
		"$(DATABASE_FILE)-wal"
	@echo "Yerel SQLite veritabanı silindi."

health:
	curl --silent --show-error "$(HOST)/"
	@echo

collect:
	@if [ -z "$(ID)" ]; then \
		echo "Kullanım: make collect ID=tQgMPzcAAAAJ"; \
		exit 1; \
	fi
	curl --silent --show-error \
		--request POST \
		--header "Content-Type: application/json" \
		--data '{"Identifiers":["$(ID)"],"UseTestIdentifiers":false}' \
		"$(HOST)/Services/AcademicPerformance/Researcher/Collect"
	@echo

random:
	curl --silent --show-error \
		--request POST \
		--header "Content-Type: application/json" \
		--data '{}' \
		"$(HOST)/Services/AcademicPerformance/Researcher/Random"
	@echo
