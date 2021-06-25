import { Link, useHistory } from "react-router-dom";
import React, { useEffect, useState } from "react";
import base64url from "base64url";
import { IconButton, PrimaryButton } from "@fluentui/react";
import { constructAPICacheKeyFromRouteKey } from "common/Utility";
import { useRouteKeyContext } from "context/RouteContext";
import { TextLink } from "components/TextLink/TextLink";
import { TNode } from "common/Interfaces";
export interface ITreeNodeProps {
  routeKey: string;
  callTreeNodeId: string;
  node: TNode;
  indent: number;
  autoExpand: boolean;
}

interface ITreeNodeState {
  isCollapsed: boolean;
  children: TNode[];
}

const TreeNode: React.FC<ITreeNodeProps> = (props) => {
  const { node, callTreeNodeId, indent, autoExpand, routeKey } = props;
  // this is a recursive component.. we will pass it in as props rather than taking from context
  const { setRouteKey } = useRouteKeyContext();
  const history = useHistory();

  const [treeNode, setTreeNode] = useState<ITreeNodeState>({
    children: [],
    isCollapsed: true,
  });

  const expandTreeNode = (_routeKey: string, _callTreeNodeId: string, _path: string) => {
    fetch(
      "/api/callerchildren?" +
        constructAPICacheKeyFromRouteKey(_routeKey) +
        "&name=" +
        _callTreeNodeId +
        "&path=" +
        _path
    )
      .then((res) => res.json())
      .then((data) => {
        if (data !== null) {
          //ROOT <-- returns null data
          setTreeNode({
            children: data,
            isCollapsed: false,
          });
        }
      });
  };

  useEffect(() => {
    if (autoExpand === true) {
      expandTreeNode(routeKey, callTreeNodeId, node.path);
    }
  }, [callTreeNodeId, node.path]);

  const collapseTreeNode = () => {
    setTreeNode({
      children: [],
      isCollapsed: true,
    });
  };

  const handleDrillIntoClick = (d: string, path: string) => {
    let drillType = "/api/drillinto/exclusive?";
    if (d === "i") {
      drillType = "/api/drillinto/inclusive?";
    }
    fetch(drillType + constructAPICacheKeyFromRouteKey(routeKey) + "&name=" + callTreeNodeId + "&path=" + path)
      .then((res) => res.json())
      .then((data) => {
        console.log(data);
        const newRouteKey = JSON.parse(base64url.decode(routeKey));
        console.log(newRouteKey);
        newRouteKey.k = data;
        setRouteKey(base64url.encode(JSON.stringify(newRouteKey)));
        history.push(`/ui/stackviewer/hotspots/${base64url.encode(JSON.stringify(newRouteKey))}`);
      });
  };

  const toggleTreeNode = () => {
    if (treeNode?.isCollapsed) {
      expandTreeNode(routeKey, callTreeNodeId, node.path);
    } else {
      collapseTreeNode();
    }
  };

  const textLinkOnClick = () => {
    history.push(`/ui/stackviewer/callers/${routeKey}/${node.base64EncodedId}`);
  };

  //const handleSourceViewer = () => {};

  const renderTreeNode = (
    routeKey: string,
    node: TNode,
    indent: number,
    callTreeNodeId: string,
    children: TNode[],
    autoExpand: boolean,
    isCollapsed: boolean,
    toggleTreeNode: () => void
  ) => {
    return (
      <React.Fragment>
        <tr>
          <td style={{ paddingLeft: indent + "px" }}>
            {node.hasChildren && (
              <IconButton
                iconProps={{
                  iconName: isCollapsed ? "BoxAdditionSolid" : "BoxSubtractSolid",
                }}
                onClick={toggleTreeNode}
              ></IconButton>
            )}
            <TextLink onClick={textLinkOnClick} content={node.name} />
          </td>
          <td className="center">
            {/* <PrimaryButton styles={{ root: { width: 10 } }} onClick={() => handleDrillIntoClick("e", node.path)}>
              {node.exclusiveCount}
            </PrimaryButton> */}
            <Link to={`/ui/sourceviewer/${routeKey}/${node.base64EncodedId}`}>[S]</Link>
          </td>
          <td className="center">{node.exclusiveMetricPercent}%</td>
          <td className="center">
            <PrimaryButton styles={{ root: { width: 10 } }} onClick={() => handleDrillIntoClick("e", node.path)}>
              {node.exclusiveCount}
            </PrimaryButton>
          </td>
          <td className="center">{node.inclusiveMetricPercent}%</td>
          <td className="center">
            <PrimaryButton onClick={() => handleDrillIntoClick("i", node.path)}>{node.inclusiveCount}</PrimaryButton>
          </td>
          <td className="center">{node.exclusiveFoldedMetric}</td>
          <td className="center">{node.inclusiveMetricByTimeString}</td>
          <td className="center">{node.firstTimeRelativeMSec}</td>
          <td className="center">{node.lastTimeRelativeMSec}</td>
        </tr>
        {children.map((child) => {
          return (
            <TreeNode
              key={`${child.base64EncodedId}`}
              routeKey={routeKey}
              node={child}
              indent={indent + 10}
              callTreeNodeId={callTreeNodeId}
              autoExpand={autoExpand}
            ></TreeNode>
          );
        })}
      </React.Fragment>
    );
  };
  return renderTreeNode(
    routeKey,
    node,
    indent,
    callTreeNodeId,
    node.hasChildren ? treeNode.children : [],
    node.hasChildren && treeNode.children.length === 1,
    treeNode.isCollapsed,
    toggleTreeNode
  );
};

export { TreeNode };
