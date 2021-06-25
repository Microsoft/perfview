import { IColumn } from "@fluentui/react";

export const ProcessChooserColDef: IColumn[] = [
  {
    key: "Process Name",
    name: "Process Name",
    isResizable: true,
    fieldName: "name",
    minWidth: 200,
  },
  {
    key: "cpumSec",
    name: "CPU MSec",
    fieldName: "cpumSec",
    minWidth: 200,
  },
];
