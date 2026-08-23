HOST ?= http://localhost:5000
DATABASE_FILE := $(CURDIR)/academic.db
STORAGE_DIR := $(CURDIR)/Storage
empty :=
space := $(empty) $(empty)
comma := ,

.PHONY: help run build clean health collect random

help:
	@echo "Kullanılabilir komutlar:"
	@echo "  make run                  HTTP sunucusunu başlatır"
	@echo "  make build                Projeyi derler"
	@echo "  make clean                SQLite veritabanını ve Storage klasörünü siler"
	@echo "  make health               Sunucunun çalıştığını kontrol eder"
	@echo "  make collect ID=...       ORCID ile akademisyen yayınlarını sorgular"
	@echo "  make random               Rastgele akademisyen özeti getirir"
	@echo "  make health HOST=...      Farklı bir sunucu adresi kullanır"

run:
	dotnet run

build:
	dotnet build

clean:
	@if [ -f "$(DATABASE_FILE)" ] && lsof "$(DATABASE_FILE)" >/dev/null 2>&1; then \
		echo "Veritabanı şu anda bir uygulama tarafından kullanılıyor:"; \
		lsof -nP "$(DATABASE_FILE)"; \
		echo; \
		echo "Yukarıdaki uygulamada veritabanı bağlantısını kapatıp tekrar dene."; \
		exit 1; \
	fi
	rm -f -- \
		"$(DATABASE_FILE)" \
		"$(DATABASE_FILE)-shm" \
		"$(DATABASE_FILE)-wal"
	@if [ -d "$(STORAGE_DIR)" ]; then \
		find "$(STORAGE_DIR)" -depth -mindepth 1 -delete; \
		rmdir "$(STORAGE_DIR)"; \
	fi
	@echo "Yerel SQLite veritabanı ve Storage klasörü silindi."

health:
	curl --silent --show-error "$(HOST)/"
	@echo

collect:
	@if [ -z "$(strip $(ID))" ]; then \
		echo 'Kullanım: make collect ID="0000-0001-8560-7482"'; \
		exit 1; \
	fi
	@response_file="$$(mktemp -t academic-collect.XXXXXX)"; \
	start_time="$$(date +%s)"; \
	echo "Toplama isteği gönderildi. Akademik kaynaklar bekleniyor..."; \
	curl --silent --show-error \
		--fail-with-body \
		--request POST \
		--header "Content-Type: application/json" \
		--data '{"Identifiers":["$(subst $(space),"$(comma)",$(strip $(ID)))"],"UseTestIdentifiers":false}' \
		"$(HOST)/Services/AcademicPerformance/Researcher/CollectText" \
		> "$$response_file" & \
	request_pid="$$!"; \
	while kill -0 "$$request_pid" 2>/dev/null; do \
		current_time="$$(date +%s)"; \
		elapsed_seconds="$$((current_time - start_time))"; \
		printf '\rİşlem: %s saniye' "$$elapsed_seconds"; \
		sleep 1; \
	done; \
	if wait "$$request_pid"; then \
		printf '\rİşlem tamamlandı.                                  \n'; \
		cat "$$response_file"; \
		echo; \
		status=0; \
	else \
		status="$$?"; \
		printf '\rİstek hata ile tamamlandı.                          \n'; \
		cat "$$response_file"; \
		echo; \
	fi; \
	rm -f -- "$$response_file"; \
	exit "$$status"

random:
	curl --silent --show-error \
		--request POST \
		--header "Content-Type: application/json" \
		--data '{}' \
		"$(HOST)/Services/AcademicPerformance/Researcher/Random"
	@echo
