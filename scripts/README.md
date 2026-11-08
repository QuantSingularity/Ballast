# Scripts

| Script                 | What it does                                                                                                      |
| ---------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `setup_environment.sh` | Checks .NET 10 and Node 22, installs web packages, creates `.env` with a generated secret                         |
| `build.sh`             | Builds the solution, the Angular app and the mobile UI library (`--publish` also publishes the API to `dist/api`) |
| `run_tests.sh`         | Runs the AI model, backend, mobile UI and web suites and prints a summary                                         |
| `start_dev.sh`         | Starts the API on :5000 and the Angular dev server on :4200                                                       |
| `smoke_test.sh`        | Publishes the API, starts it, signs in and calls the main endpoints                                               |
| `lint_code.sh`         | `dotnet format`, prettier, shellcheck and a text policy check (`--fix` applies formatting)                        |
| `docker_build.sh`      | Builds the backend and web images                                                                                 |
| `deploy_automation.sh` | Applies a Kubernetes overlay (dry run unless `--apply`)                                                           |
| `release.sh`           | Tests, builds and zips a release into `dist/`                                                                     |

`lib.sh` holds shared helpers and is sourced by the others.
