import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { ObjectSummary, ObjectDetail } from '../api/types'
import { fetchRoots, fetchChildren } from '../api/tree'
import { searchObjects, getObject } from '../api/objects'

/** Map backend TreeNode to PrimeVue Tree node format */
function toTreeNode(node: { dn: string; name: string; objectClass: string; hasChildren: boolean; icon: string }) {
  return {
    key: node.dn,
    label: node.name,
    data: node,
    leaf: !node.hasChildren,
    icon: node.icon ? `pi ${node.icon}` : 'pi pi-folder',
    children: undefined as any[] | undefined,
  }
}

export const useDirectoryStore = defineStore('directory', () => {
  const treeNodes = ref<any[]>([])
  const selectedNodeKey = ref<string | null>(null)
  const selectedNodeDn = ref<string | null>(null)
  const objects = ref<ObjectSummary[]>([])
  const selectedObject = ref<ObjectDetail | null>(null)
  const loading = ref(false)
  const totalCount = ref(0)

  async function loadRoots() {
    loading.value = true
    try {
      const roots = await fetchRoots()
      treeNodes.value = roots.map(toTreeNode)
    } finally {
      loading.value = false
    }
  }

  async function loadChildren(parentNode: any) {
    const nodes = await fetchChildren(parentNode.data.dn)
    parentNode.children = nodes.map(toTreeNode)
    return parentNode.children
  }

  async function loadObjects(baseDn: string, filter?: string, pageSize?: number) {
    loading.value = true
    try {
      const result = await searchObjects(baseDn, filter, pageSize || 100)
      objects.value = result.items
      totalCount.value = result.totalCount
    } finally {
      loading.value = false
    }
  }

  async function loadObjectDetail(guid: string) {
    loading.value = true
    try {
      selectedObject.value = await getObject(guid)
    } finally {
      loading.value = false
    }
  }

  function clearObjects() {
    objects.value = []
    totalCount.value = 0
  }

  return {
    treeNodes,
    selectedNodeKey,
    selectedNodeDn,
    objects,
    selectedObject,
    loading,
    totalCount,
    loadRoots,
    loadChildren,
    loadObjects,
    loadObjectDetail,
    clearObjects,
  }
})
