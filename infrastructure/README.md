# Infrastructure

| Path                             | Purpose                                                                                                       |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| `docker/Dockerfile.backend`      | Multi-stage build of the ASP.NET Core API, runs as the non-root `app` user                                    |
| `docker/Dockerfile.ai`           | Python model service (numpy, FastAPI), non-root, health check on `/health`                                    |
| `docker/Dockerfile.frontend`     | Angular production build served by unprivileged nginx, proxies `/api` to the backend                          |
| `docker-compose.yml`             | Model service, backend and web. Requires `BALLAST_SECRET_KEY`. The model service is not published to the host |
| `docker-compose.override.yml`    | Exposes the API on port 5000 for local development                                                            |
| `kubernetes/base`                | Namespace, config, PVC, deployments (model service, backend, web), services, ingress, network policies        |
| `kubernetes/overlays/{dev,prod}` | Kustomize overlays                                                                                            |
| `validate.sh`                    | YAML syntax check, plus compose and kustomize checks when those tools exist                                   |

## Run locally with Docker

```bash
export BALLAST_SECRET_KEY="$(openssl rand -hex 32)"
docker compose -f infrastructure/docker-compose.yml -f infrastructure/docker-compose.override.yml up --build
# web: http://localhost:4200   api: http://localhost:5000
```

## Kubernetes

```bash
kubectl create namespace ballast
kubectl -n ballast create secret generic ballast-secrets --from-literal=BALLAST_SECRET_KEY="$(openssl rand -hex 32)"
kubectl apply -k infrastructure/kubernetes/overlays/prod
```

The model service and backend deployments are deliberately single replica with a `Recreate` rollout. The model service holds the
fitted anomaly detector in memory (the backend refits it automatically after a restart), and a network policy admits only the
backend. The backend is a single replica because its state lives in a JSON snapshot on a
`ReadWriteOnce` volume, so several replicas would diverge. Horizontal scaling needs the `BallastDb` abstraction replaced
by a shared database first.

## Verification status

The YAML in this directory is syntax-checked by `validate.sh`. The images and manifests have not been built or applied
in the environment where this repository was developed (no Docker daemon or cluster was available), so treat them as a
reviewed starting point. Terraform and Ansible modules that exist in sibling repositories are not included because they
are specific to a cloud account.
