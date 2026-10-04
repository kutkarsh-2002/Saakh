# Two-stage build, per tech-stack.md: a node stage runs the production Angular
# build, then the compiled output is copied into a lean nginx:alpine image.

FROM node:22-alpine AS build
WORKDIR /app

# Dependencies are installed from the lockfile first, so this layer is cached
# across source-only changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build -- --configuration production

FROM nginx:alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html

EXPOSE 80

# 127.0.0.1, not localhost: inside the container localhost resolves to ::1 first
# and nginx listens on IPv4 only, so the probe is refused forever and the
# container sits permanently unhealthy while serving traffic perfectly well.
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -q --spider http://127.0.0.1/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
