import { RouteComponentProps, useHistory } from "react-router";
import { Link } from "react-router-dom";
import React, { useEffect, useState } from "react";
import { TreeNode } from "../TreeNode/TreeNode";
import base64url from "base64url";
import { constructAPICacheKeyFromRouteKey } from "common/Utility";
import { StackViewerFilter } from "features/StackViewerFilter/StackViewerFilter";
import { useRouteKeyContext } from "context/RouteContext";
import { ScrollablePane } from "@fluentui/react";
import { TNode } from "common/Interfaces";

interface MatchParams {
  callTreeNodeId: string;
  subtree?: string;
}

export interface ICallersProps extends RouteComponentProps<MatchParams> {}

const Callers: React.FC<ICallersProps> = (props) => {
  const { match } = props;
  const [node, setNode] = useState<TNode>();
  const { routeKey } = useRouteKeyContext();
  const history = useHistory();

  useEffect(() => {
    fetch(
      "/api/treenode?" +
        constructAPICacheKeyFromRouteKey(routeKey) +
        "&name=" +
        match.params.callTreeNodeId +
        "&subtree=" +
        match.params.subtree
    )
      .then((res) => res.json())
      .then((data) => {
        if (data === null) {
          history.push(`/ui/stackviewer/hotspots/${routeKey}`);
        } else {
          data.hasChildren = true; // HACK: Because the api doesn't return this set true.
          setNode(data);
        }
      });
  }, [routeKey, match.params.callTreeNodeId, match.params.subtree]);

  const renderCallersTable = (routeKey: string, callTreeNodeId: string, node: TNode) => {
    return (
      <table className="table table-striped table-bordered" id="pd">
        <thead>
          <tr>
            <td className="center">Name</td>
            <td className="right">Source</td>
            <td className="center">Exclusive Metric %</td>
            <td className="center">Exclusive Count</td>
            <td className="center">Inclusive Metric %</td>
            <td className="center">Inclusive Count</td>
            <td className="center">Fold Count</td>
            <td className="center">When</td>
            <td className="center">First</td>
            <td className="center">Last</td>
          </tr>
        </thead>
        <tbody>
          <TreeNode
            routeKey={routeKey}
            callTreeNodeId={callTreeNodeId}
            node={node}
            indent={0}
            autoExpand={node.hasChildren}
          />
        </tbody>
      </table>
    );
  };

  const CallersTable = node && renderCallersTable(routeKey, match.params.callTreeNodeId, node);

  return (
    <div>
      <h4>
        {base64url.decode(JSON.parse(base64url.decode(routeKey)).l)} &raquo;{" "}
        <Link to={`/ui/stackviewer/hotspots/${routeKey}`}>Hotspots</Link> &raquo; Callers
      </h4>
      <StackViewerFilter />
      <div
        style={{
          height: "60vh",
          position: "relative",
          maxHeight: "inherit",
        }}
      >
        <ScrollablePane
          scrollContainerFocus={true}
          //styles={scrollablePaneStyles}
        >
          {CallersTable}
        </ScrollablePane>
      </div>
    </div>
  );
};

export { Callers };
