export interface Module {
  modulePath: string;
  id: number;
  addrCount: number;
}

export interface Process {
  id: number;
  processId: number;
  parentId: number;
  name: string;
  commandLine: string;
  cpumSec: number;
}

export interface TNode {
  exclusiveMetricPercent: number;
  inclusiveMetricPercent: number;
  hasChildren: boolean;
  exclusiveFoldedMetric: number;
  inclusiveMetricByTimeString: string;
  firstTimeRelativeMSec: number;
  lastTimeRelativeMSec: number;
  exclusiveCount: number;
  inclusiveCount: number;
  base64EncodedId: string;
  name: string;
  path: string;
  autoExpand: boolean;
}

export class EmptyTNode implements TNode {
  exclusiveMetricPercent: number;
  inclusiveMetricPercent: number;
  hasChildren: boolean;
  exclusiveFoldedMetric: number;
  inclusiveMetricByTimeString: string;
  firstTimeRelativeMSec: number;
  lastTimeRelativeMSec: number;
  exclusiveCount: number;
  inclusiveCount: number;
  base64EncodedId: string;
  name: string;
  path: string;
  autoExpand: boolean;

  constructor() {
    this.exclusiveCount = 0;
    this.inclusiveCount = 0;
    this.base64EncodedId = "";
    this.name = "";
    this.path = "";
    this.firstTimeRelativeMSec = 0;
    this.lastTimeRelativeMSec = 0;
    this.inclusiveMetricByTimeString = "";
    this.exclusiveFoldedMetric = 0;
    this.hasChildren = false;
    this.exclusiveMetricPercent = 0;
    this.inclusiveMetricPercent = 0;
    this.autoExpand = false;
  }
}
