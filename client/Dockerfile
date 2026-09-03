# syntax=docker/dockerfile:1.7

# --- build stage ---
# Installs deps and builds the Vite SPA into static assets.
FROM node:20-slim AS build

WORKDIR /app

# Copy manifests first to leverage Docker's layer cache for npm install.
COPY client/package.json client/package-lock.json ./

# Build-time env: the SPA is configured to call the API over the container network.
# In compose the client is served by nginx on 5173 and proxies/calls the api service.
ARG VITE_API_BASE_URL=http://localhost:5112
ARG VITE_HUB_URL=http://localhost:5112/hubs/game
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL}
ENV VITE_HUB_URL=${VITE_HUB_URL}

RUN npm ci

# Copy the rest of the client source and build.
COPY client/ ./
RUN npm run build

# --- runtime stage ---
# Serve the built static assets with nginx.
FROM nginx:alpine AS runtime

# Alpine's busybox provides wget for a robust healthcheck.
RUN apk add --no-cache wget

COPY --from=build /app/dist /usr/share/nginx/html

# Custom nginx config so the SPA routes fall back to index.html and the
# healthcheck path resolves. Binds IPv4 on the port the client expects (5173).
COPY client/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 5173

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD wget --quiet --tries=1 --spider http://127.0.0.1:5173/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
