.PHONY: proto install lint test test-unit test-integration run

proto:
	bash scripts/generate_proto.sh

install:
	poetry install

lint:
	poetry run ruff check src/ tests/
	poetry run mypy src/

test:
	poetry run pytest tests/ --cov=src --cov-report=term-missing

test-unit:
	poetry run pytest tests/unit/

test-integration:
	poetry run pytest tests/integration/

run:
	poetry run python -m src.main
