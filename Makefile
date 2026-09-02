HOST ?= http://localhost:5001

.PHONY: help run build clean health collect

help:
	@echo "Kullanılabilir komutlar:"
	@echo "  make run                  HTTP sunucusunu başlatır"
	@echo "  make build                Projeyi derler"
	@echo "  make clean                .NET build çıktılarını temizler"
	@echo "  make health               Sunucunun çalıştığını kontrol eder"
	@echo "  make collect ID=...       ORCID, Scholar ID ve/veya ResearcherID ile veri toplar"
	@echo "  make health HOST=...      Farklı bir sunucu adresi kullanır"

run:
	dotnet run

build:
	dotnet build

clean:
	dotnet clean
	@echo "Build çıktıları temizlendi. SQL Server veritabanına dokunulmadı."

health:
	curl --silent --show-error "$(HOST)/"
	@echo

collect:
	@if [ -z "$(strip $(ID))" ]; then \
		echo 'Kullanım: make collect ID="0000-0001-8560-7482 A-1009-2008"'; \
		exit 1; \
	fi
	@response_file="$$(mktemp -t academic-collect.XXXXXX)"; \
	orcid=""; scholar_id=""; researcher_id=""; \
	for identifier in $(strip $(ID)); do \
		case "$$identifier" in \
			????-????-????-????) orcid="$$identifier" ;; \
			[A-Za-z]*-????-????) researcher_id="$$identifier" ;; \
			*) scholar_id="$$identifier" ;; \
		esac; \
	done; \
	request_body='{'; separator=''; \
	if [ -n "$$orcid" ]; then \
		request_body="$${request_body}$${separator}\"Orcid\":\"$${orcid}\""; separator=','; \
	fi; \
	if [ -n "$$scholar_id" ]; then \
		request_body="$${request_body}$${separator}\"GoogleScholarId\":\"$${scholar_id}\""; separator=','; \
	fi; \
	if [ -n "$$researcher_id" ]; then \
		request_body="$${request_body}$${separator}\"WebOfScienceResearcherId\":\"$${researcher_id}\""; \
	fi; \
	request_body="$${request_body}}"; \
	start_time="$$(date +%s)"; \
	echo "Toplama isteği gönderildi. Akademik kaynaklar bekleniyor..."; \
	curl --silent --show-error \
		--fail-with-body \
		--request POST \
		--header "Content-Type: application/json" \
		--data "$$request_body" \
		"$(HOST)/Services/AcademicPerformance/V1/Collect" \
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
