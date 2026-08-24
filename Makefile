HOST ?= http://localhost:5001
DATABASE_FILE := $(CURDIR)/academic.db
empty :=
space := $(empty) $(empty)
comma := ,

.PHONY: help run build clean health collect

help:
	@echo "Kullanılabilir komutlar:"
	@echo "  make run                  HTTP sunucusunu başlatır"
	@echo "  make build                Projeyi derler"
	@echo "  make clean                Yerel SQLite veritabanını siler"
	@echo "  make health               Sunucunun çalıştığını kontrol eder"
	@echo "  make collect ID=...       ORCID ve/veya ResearcherID ile veri toplar"
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
	@echo "Yerel SQLite veritabanı silindi."

health:
	curl --silent --show-error "$(HOST)/"
	@echo

collect:
	@if [ -z "$(strip $(ID))" ]; then \
		echo 'Kullanım: make collect ID="0000-0001-8560-7482 A-1009-2008"'; \
		exit 1; \
	fi
	@response_file="$$(mktemp -t academic-collect.XXXXXX)"; \
	start_time="$$(date +%s)"; \
	echo "Toplama isteği gönderildi. Akademik kaynaklar bekleniyor..."; \
	curl --silent --show-error \
		--fail-with-body \
		--request POST \
		--header "Content-Type: application/json" \
		--data '{"Identifiers":["$(subst $(space),"$(comma)",$(strip $(ID)))"]}' \
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
