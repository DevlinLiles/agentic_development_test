# ChessMvp client — Vite/React build served by nginx.
# Build context is the repository root (see docker-compose.yml).

FROM node:22-alpine AS build
WORKDIR /repo/client

COPY client/package.json client/package-lock.json ./
RUN npm ci

# Vite bakes VITE_* values into the bundle at build time via import.meta.env, so the
# browser-visible host URLs must be provided here, not at runtime.
ARG VITE_API_BASE_URL=http://localhost:5112
ARG VITE_HUB_URL=http://localhost:5112/hubs/game
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL \
    VITE_HUB_URL=$VITE_HUB_URL

COPY client/ ./
RUN npm run build

FROM nginx:alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/client/dist /usr/share/nginx/html

EXPOSE 80
