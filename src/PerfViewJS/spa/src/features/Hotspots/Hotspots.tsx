import React, { useEffect, useRef, useState } from "react";
import { StackViewerFilter } from "../StackViewerFilter/StackViewerFilter";
import base64url from "base64url";
import { CheckboxVisibility, DetailsList, PrimaryButton } from "@fluentui/react";

import { Container, Row } from "react-grid-system";
import { constructAPICacheKeyFromRouteKey, copyAndSort } from "common/Utility";
import { useRouteKeyContext } from "context/RouteContext";

import { IColumn } from "@fluentui/react";
import { useHistory } from "react-router";
import { TextLink } from "components/TextLink/TextLink";
import { TNode } from "common/Interfaces";

const Hotspots: React.FC = () => {
  const [nodes, setNodes] = useState<TNode[]>([]);
  const refNodes = useRef(nodes); //https://stackoverflow.com/a/64572688/670514
  const [colDef, setColDef] = useState<IColumn[]>([]);
  const refColDef = useRef(colDef); //https://stackoverflow.com/a/64572688/670514

  const history = useHistory();
  const { routeKey, setRouteKey } = useRouteKeyContext();

  const updateNodes = (newNodes: TNode[]) => {
    refNodes.current = newNodes;
    setNodes(newNodes);
  };

  const updateColumns = (newColumns: IColumn[]) => {
    refColDef.current = newColumns;
    setColDef(newColumns);
  };

  useEffect(() => {
    fetch("/api/hotspots?" + constructAPICacheKeyFromRouteKey(routeKey))
      .then((res) => res.json())
      .then((data) => {
        updateNodes(data);
        updateColumns(HotspotsColDef);
      });
  }, [routeKey]);

  const onColumnClick = (ev: React.MouseEvent<HTMLElement>, column: IColumn): void => {
    const newColumns: IColumn[] = refColDef.current.slice();
    const currColumn: IColumn = newColumns.filter((currCol) => column.key === currCol.key)[0];
    newColumns.forEach((newCol: IColumn) => {
      if (newCol === currColumn) {
        currColumn.isSortedDescending = !currColumn.isSortedDescending;
        currColumn.isSorted = true;
      } else {
        newCol.isSorted = false;
        newCol.isSortedDescending = true;
      }
    });
    // eslint-disable-next-line @typescript-eslint/no-non-null-assertion
    const sortedNodes = copyAndSort(refNodes.current, currColumn.fieldName!, currColumn.isSortedDescending);
    updateNodes(sortedNodes);
    updateColumns(newColumns);
  };

  const HotspotsColDef: IColumn[] = [
    {
      key: "Name",
      name: "Name",
      isResizable: true,
      fieldName: "name",
      minWidth: 1000,
      onColumnClick: onColumnClick,
    },
    {
      key: "exclusiveMetricPercent",
      name: "Exclusive Metric %",
      fieldName: "exclusiveMetricPercent",
      minWidth: 120,
      onColumnClick: onColumnClick,
    },
    {
      key: "exclusiveCount",
      name: "Exclusive Count",
      fieldName: "exclusiveCount",
      minWidth: 120,
      onColumnClick: onColumnClick,
    },
    {
      key: "inclusiveMetricPercent",
      name: "Inclusive Metric %",
      fieldName: "inclusiveMetricPercent",
      minWidth: 120,
      onColumnClick: onColumnClick,
    },
    {
      key: "inclusiveCount",
      name: "Inclusive Count",
      fieldName: "inclusiveCount",
      minWidth: 120,
      onColumnClick: onColumnClick,
    },
    {
      key: "exclusiveFoldedMetric",
      name: "Fold Count",
      fieldName: "exclusiveFoldedMetric",
      minWidth: 80,
      onColumnClick: onColumnClick,
    },
    {
      key: "inclusiveMetricByTimeString",
      name: "When",
      isResizable: true,
      fieldName: "inclusiveMetricByTimeString",
      minWidth: 250,
      onColumnClick: onColumnClick,
    },
    {
      key: "firstTimeRelativeMSec",
      name: "First",
      fieldName: "firstTimeRelativeMSec",
      minWidth: 100,
      onColumnClick: onColumnClick,
    },
    {
      key: "lastTimeRelativeMSec",
      name: "Last",
      fieldName: "lastTimeRelativeMSec",
      minWidth: 100,
      onColumnClick: onColumnClick,
    },
  ];

  const renderItemColumn = (item?: TNode, index?: number, column?: IColumn) => {
    if (column?.fieldName === "exclusiveCount") {
      return (
        <PrimaryButton onClick={() => handleDrillIntoClick("e", item?.base64EncodedId)}>
          {item?.exclusiveCount}
        </PrimaryButton>
      );
    } else if (column?.fieldName === "inclusiveCount") {
      return (
        <PrimaryButton onClick={() => handleDrillIntoClick("i", item?.base64EncodedId)}>
          {item?.inclusiveCount}
        </PrimaryButton>
      );
    } else if (column?.fieldName === "name") {
      return (
        <TextLink
          onClick={() => {
            history.push(`/ui/stackviewer/callers/${routeKey}/${item?.base64EncodedId}`);
          }}
          content={item?.name || ""}
        />
      );
    } else {
      //? everything is optional..
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      //@ts-ignore
      return item[column.fieldName];
    }
  };

  const handleDrillIntoClick = (d: string, nodeBase64EncodedId?: string) => {
    let drillType = "/api/drillinto/exclusive?";
    if (d === "i") {
      drillType = "/api/drillinto/inclusive?";
    }

    fetch(drillType + constructAPICacheKeyFromRouteKey(routeKey) + "&name=" + nodeBase64EncodedId)
      .then((res) => res.json())
      .then((data) => {
        const newRouteKey = JSON.parse(base64url.decode(routeKey));
        newRouteKey.k = data;
        setRouteKey(base64url.encode(JSON.stringify(newRouteKey)));
      });
  };

  return (
    <Container>
      <Row>
        <h4>{base64url.decode(JSON.parse(base64url.decode(routeKey)).l)} &raquo; Hotspots</h4>
      </Row>
      <StackViewerFilter />
      <Row>
        <DetailsList
          checkboxVisibility={CheckboxVisibility.hidden}
          setKey={"key"}
          compact={true}
          items={nodes}
          columns={colDef}
          onRenderItemColumn={renderItemColumn}
        />
      </Row>
    </Container>
  );
};

export { Hotspots };
