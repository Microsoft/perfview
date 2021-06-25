import { getTheme } from "@fluentui/react";
import React from "react";
import { Text } from "@fluentui/react";
const theme = getTheme();

const textLinkStyle = {
  root: {
    color: theme.semanticColors.primaryButtonBackground,
    "text-decoration": "underline",
    cursor: "pointer",
    // "&:hover": {
    //   "text-decoration": "underline",
    // },
  },
};

interface ITextLinkProps {
  content: string;
  onClick: () => void;
}

const TextLink: React.FC<ITextLinkProps> = (props) => {
  const { content, onClick } = props;
  return (
    <Text styles={textLinkStyle} onClick={onClick}>
      {content}
    </Text>
  );
};

export { TextLink };
