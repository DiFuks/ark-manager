APP  := ArkManager.app
DIST := dist/$(APP)
DEST := /Applications/$(APP)

.PHONY: build bundle run clean

# Собрать .app, установить в /Applications (с заменой) и убрать за собой dist/
build: bundle
	@echo "==> Установка в /Applications (с заменой)"
	@rm -rf "$(DEST)"
	@cp -R "$(DIST)" "$(DEST)"
	@# best-effort очистка dist/ с ретраем: Spotlight/Finder может воссоздать .DS_Store
	@# в свежем .app-бандле прямо во время удаления → "Directory not empty"
	@for i in 1 2 3; do rm -rf dist 2>/dev/null; [ -d dist ] || break; sleep 1; done; true
	@echo "Установлено: $(DEST)"

# Только собрать бандл в dist/ (без установки)
bundle:
	@./build-app.sh

# Запустить установленную версию
run:
	@open "$(DEST)"

# Удалить артефакты сборки
clean:
	@rm -rf dist
	@echo "dist/ удалён"
