.PHONY: build mac linux windows run clean

build:
	@./build.sh

mac:
	@./build.sh --target macos

linux:
	@./build.sh --target linux

windows:
	@./build.sh --target windows

# Open the built .app from dist/
run:
	@open "dist/$(shell awk -F '[<>]' '/<Version>/{print $$3; exit}' Directory.Build.props | xargs -I{} echo "ArkManager-{}-macos-arm64")/ArkManager.app"

clean:
	@rm -rf dist
	@echo "dist/ removed"
