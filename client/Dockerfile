# Build stage
FROM node:22-bookworm-slim AS build

WORKDIR /app

# Copy dependency manifests first to cache npm install.
COPY package.json package-lock.json ./

RUN npm ci

# Copy the rest of the source and build.
COPY . .

# Build-time env: where the SPA finds the API and SignalR hub at runtime in compose.
ARG VITE_API_BASE_URL=http://localhost:5112
ARG VITE_HUB_URL=http://localhost:5112/hubs/game

RUN npm run build

# Runtime stage: serve the built SPA with nginx.
FROM nginx:alpine AS runtime

# Install curl for a reliable healthcheck (alpine's busybox wget also works, but curl is explicit).
RUN apk add --no-cache curl

# Remove the default static site and copy our build output.
RUN rm -rf /usr/share/nginx/html/*
COPY --from=build /app/dist /usr/share/nginx/html

# Custom nginx config so SPA routing works.
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=10s --timeout=5s --retries=3 --start-period=10s \
  CMD curl --fail http://127.0.0.1:80/ || exit 1

CMD ["nginx", "-g", "daemon off;"]
