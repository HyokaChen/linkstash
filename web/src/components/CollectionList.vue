<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { toast } from 'vue-sonner'
import { api, type CollectionItem } from '@/api/client'
import LinkCard from '@/components/LinkCard.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const items = ref<CollectionItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20
const search = ref('')
const loading = ref(false)

async function load() {
  loading.value = true
  try {
    const params = new URLSearchParams({ page: String(page.value), pageSize: String(pageSize) })
    if (search.value.trim()) params.set('search', search.value.trim())
    const data = await api<{ items: CollectionItem[]; total: number; page: number; pageSize: number }>(
      `/api/collections?${params.toString()}`,
    )
    items.value = data.items
    total.value = data.total
  } catch (e: unknown) {
    const err = e as { message?: string }
    toast.error(`加载失败：${err.message || '未知错误'}`)
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="space-y-3">
    <div class="flex gap-2">
      <Input v-model="search" placeholder="搜索标题 / 翻译 / URL" @keyup.enter="page = 1; load()" />
      <Button variant="outline" @click="page = 1; load()">搜索</Button>
    </div>
    <p v-if="total" class="text-sm text-muted-foreground">共 {{ total }} 条</p>
    <p v-else-if="!loading" class="py-8 text-center text-sm text-muted-foreground">
      收藏库为空，粘贴 URL 开始收藏
    </p>
    <LinkCard v-for="item in items" :key="item.id" :item="item" @deleted="load" />
    <div v-if="total > pageSize" class="flex items-center justify-between">
      <Button size="sm" variant="outline" :disabled="page <= 1" @click="page--; load()">上一页</Button>
      <span class="text-sm text-muted-foreground">第 {{ page }} / {{ Math.ceil(total / pageSize) }} 页</span>
      <Button size="sm" variant="outline" :disabled="page * pageSize >= total" @click="page++; load()">下一页</Button>
    </div>
  </div>
</template>