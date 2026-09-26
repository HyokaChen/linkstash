<script setup lang="ts">
import { RouterView } from 'vue-router'
import { Toaster } from '@/components/ui/sonner'
</script>

<template>
  <div class="min-h-svh bg-background text-foreground">
    <RouterView />
    <Toaster rich-colors position="top-center" />
  </div>
</template>