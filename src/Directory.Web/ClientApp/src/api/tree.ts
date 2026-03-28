import { get } from './client'
import type { TreeNode } from './types'

export const fetchRoots = () => get<TreeNode[]>('/tree/roots')

export const fetchChildren = (parentDn: string) =>
  get<TreeNode[]>(`/tree/children?parentDn=${encodeURIComponent(parentDn)}`)
